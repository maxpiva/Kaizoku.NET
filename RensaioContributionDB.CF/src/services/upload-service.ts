import type { UploadItem } from '../models/requests';
import type { UploadError, UploadResponse } from '../models/responses';
import { SUPPORTED_ACTIONS, SUPPORTED_ENTITY_TYPES } from '../types';
import { base64ToBlob } from '../utils/binary';

/**
 * Process a batch of upload items for a single contributor.
 *
 * Actions:
 *   - `add` — unconditional UPSERT by identity key:
 *       * record missing → insert
 *       * record exists  → update (values + ownership transfer to caller)
 *       * record exists with IDENTICAL content (content_hash match) → skip
 *         entirely: no write, no ownership transfer. This kills the D1 write
 *         cost of every contributor sweep re-uploading unchanged rows.
 *   - `remove` — soft-delete ANY active record by identity key.
 *
 * Identity keys:
 *   - source:  `id` (contributor-provided identifier, DB PK)
 *   - metadata: title + metadata_provider + metadata_provider_key
 *
 * Titles are resolved server-side (reuse active title by name, or create).
 * Concurrent title creation is race-safe: the UNIQUE partial index on active
 * titles + INSERT OR IGNORE + a follow-up SELECT guarantee the winning id is
 * returned instead of failing the whole batch.
 *
 * All valid statements run in a single D1 batch (atomic). Item-level
 * validation errors are collected and returned.
 */
export async function processUpload(
  db: D1Database,
  contributorId: string,
  items: UploadItem[]
): Promise<UploadResponse> {
  const now = new Date().toISOString();
  const errors: UploadError[] = [];
  const statements: D1PreparedStatement[] = [];
  const titleCache = new Map<string, string>(); // title name → title id
  let skipped = 0;

  for (let index = 0; index < items.length; index += 1) {
    const item = items[index];

    const error = validateItem(item);
    if (error) {
      errors.push({ index, message: error });
      continue;
    }

    try {
      if (item.action === 'add') {
        const statement = await buildUpsertStatement(db, item, contributorId, now, titleCache);
        if (statement) {
          statements.push(statement);
        } else {
          // Content identical to what is already stored — nothing to write.
          skipped += 1;
        }
      } else {
        // remove
        statements.push(await buildRemoveStatement(db, item, now, titleCache));
      }
    } catch (err) {
      errors.push({ index, message: err instanceof Error ? err.message : 'Unknown error' });
    }
  }

  // Execute all valid statements atomically (D1 batch is transactional).
  if (statements.length > 0) {
    await db.batch(statements);
  }

  return { processed: statements.length, skipped, errors };
}

/**
 * Resolve a title name to a title id.
 * Reuses an existing ACTIVE title with the same name, or creates a new title
 * record. Results are cached per request.
 *
 * The create path is race-safe: `INSERT OR IGNORE` plus a follow-up SELECT
 * returns the id of whichever request won the race (backed by the UNIQUE
 * partial index on active titles), so concurrent uploads never fail the batch
 * with a primary-key violation.
 */
async function resolveTitle(
  db: D1Database,
  title: string,
  titleCache: Map<string, string>
): Promise<string> {
  const cached = titleCache.get(title);
  if (cached) {
    return cached;
  }

  const existing = await db
    .prepare('SELECT id FROM titles WHERE title = ? AND archived_at IS NULL LIMIT 1')
    .bind(title)
    .first<{ id: string }>();

  if (existing) {
    titleCache.set(title, existing.id);
    return existing.id;
  }

  // Create a new title. If a concurrent request created the same title
  // between our SELECT and this INSERT, OR IGNORE swallows the conflict.
  const id = crypto.randomUUID();
  await db
    .prepare('INSERT OR IGNORE INTO titles (id, title, archived_at) VALUES (?, ?, NULL)')
    .bind(id, title)
    .run();

  // Whichever request won, resolve the actual stored id (ours or theirs).
  const row = await db
    .prepare('SELECT id FROM titles WHERE title = ? AND archived_at IS NULL LIMIT 1')
    .bind(title)
    .first<{ id: string }>();

  const resolvedId = row?.id ?? id;
  titleCache.set(title, resolvedId);
  return resolvedId;
}

/**
 * Build the upsert statement for an `add` item.
 *
 * Returns `null` when the incoming content matches the stored `content_hash`
 * (no write, ownership preserved). Otherwise returns an INSERT or UPDATE.
 *
 * IMPORTANT: `id` is the PRIMARY KEY for sources, so the existence check
 * matches REGARDLESS of `archived_at`. If the row exists (active or
 * archived), we UPDATE and clear `archived_at` (resurrect) — otherwise the
 * INSERT would violate the primary-key constraint.
 */
async function buildUpsertStatement(
  db: D1Database,
  item: UploadItem,
  contributorId: string,
  now: string,
  titleCache: Map<string, string>
): Promise<D1PreparedStatement | null> {
  const { type, data } = item;
  const dataBlob = base64ToBlob(data.data);
  const titleId = await resolveTitle(db, data.title as string, titleCache);

  switch (type) {
    case 'source': {
      const incomingHash = await hashSourceContent(titleId, dataBlob);
      const existing = await db
        .prepare('SELECT content_hash, archived_at FROM sources WHERE id = ? LIMIT 1')
        .bind(data.id)
        .first<{ content_hash: string | null; archived_at: string | null }>();

      if (!existing) {
        // insert. OR IGNORE makes a concurrent same-id insert harmless
        // instead of failing the whole batch with a PK violation.
        return db
          .prepare(
            `INSERT OR IGNORE INTO sources (id, title_id, data, contributor_id, last_change, archived_at, content_hash)
             VALUES (?, ?, ?, ?, ?, NULL, ?)`
          )
          .bind(data.id, titleId, dataBlob, contributorId, now, incomingHash);
      }

      // Identical active content → skip the write (and the ownership steal).
      if (existing.archived_at === null && existing.content_hash === incomingHash) {
        return null;
      }

      // update + ownership transfer + clear archived_at (resurrect if archived)
      return db
        .prepare(
          `UPDATE sources
           SET title_id = ?, data = ?, contributor_id = ?, last_change = ?, archived_at = NULL, content_hash = ?
           WHERE id = ?`
        )
        .bind(titleId, dataBlob, contributorId, now, incomingHash, data.id);
    }
    case 'metadata': {
      const metadataProvider = data.metadata_provider as string;
      const metadataProviderKey = data.metadata_provider_key as string;
      const linkType = data.link_type as number;
      const incomingHash = await hashMetadataContent(titleId, metadataProvider, metadataProviderKey, linkType);
      const existing = await db
        .prepare(
          `SELECT content_hash FROM metadata
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ?
             AND archived_at IS NULL
           LIMIT 1`
        )
        .bind(titleId, metadataProvider, metadataProviderKey)
        .first<{ content_hash: string | null }>();

      if (!existing) {
        // insert. OR IGNORE makes a concurrent same-identity insert harmless
        // (guarded by the UNIQUE partial index on active metadata identity).
        return db
          .prepare(
            `INSERT OR IGNORE INTO metadata (id, title_id, metadata_provider, metadata_provider_key, link_type, contributor_id, last_change, archived_at, content_hash)
             VALUES (?, ?, ?, ?, ?, ?, ?, NULL, ?)`
          )
          .bind(
            crypto.randomUUID(),
            titleId,
            metadataProvider,
            metadataProviderKey,
            linkType,
            contributorId,
            now,
            incomingHash
          );
      }

      // Identical active content → skip the write (and the ownership steal).
      if (existing.content_hash === incomingHash) {
        return null;
      }

      // update + ownership transfer
      return db
        .prepare(
          `UPDATE metadata
           SET link_type = ?, contributor_id = ?, last_change = ?, content_hash = ?
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ?
             AND archived_at IS NULL`
        )
        .bind(
          linkType,
          contributorId,
          now,
          incomingHash,
          titleId,
          metadataProvider,
          metadataProviderKey
        );
    }
    default:
      throw new Error(`Unsupported type: ${type}`);
  }
}

/**
 * Build a soft-delete statement for a `remove` item.
 */
async function buildRemoveStatement(
  db: D1Database,
  item: UploadItem,
  now: string,
  titleCache: Map<string, string>
): Promise<D1PreparedStatement> {
  const { type, data } = item;

  switch (type) {
    case 'source':
      return db
        .prepare('UPDATE sources SET archived_at = ? WHERE id = ? AND archived_at IS NULL')
        .bind(now, data.id);
    case 'metadata': {
      const titleId = await resolveTitle(db, data.title as string, titleCache);
      return db
        .prepare(
          `UPDATE metadata
           SET archived_at = ?
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ?
             AND archived_at IS NULL`
        )
        .bind(now, titleId, data.metadata_provider, data.metadata_provider_key);
    }
    default:
      throw new Error(`Unsupported type: ${type}`);
  }
}

/**
 * SHA-256 of a byte sequence, hex-encoded.
 */
async function hashHex(bytes: Uint8Array): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', bytes);
  return [...new Uint8Array(digest)]
    .map((b) => b.toString(16).padStart(2, '0'))
    .join('');
}

/**
 * Content hash for a source row: title_id + data bytes.
 * Includes title_id so re-titling an existing source id is detected.
 */
async function hashSourceContent(titleId: string, data: ArrayBuffer | null): Promise<string> {
  const titleBytes = new TextEncoder().encode(titleId);
  if (!data || data.byteLength === 0) {
    return hashHex(titleBytes);
  }
  const combined = new Uint8Array(titleBytes.length + data.byteLength);
  combined.set(titleBytes);
  combined.set(new Uint8Array(data), titleBytes.length);
  return hashHex(combined);
}

/**
 * Content hash for a metadata row: identity key + link_type.
 */
async function hashMetadataContent(
  titleId: string,
  provider: string,
  providerKey: string,
  linkType: number
): Promise<string> {
  const content = `${titleId}\u0000${provider}\u0000${providerKey}\u0000${linkType}`;
  return hashHex(new TextEncoder().encode(content));
}

/**
 * Validate a single upload item structurally.
 * Returns an error message string, or null when valid.
 */
function validateItem(item: UploadItem): string | null {
  if (!item || typeof item !== 'object') {
    return 'Item must be an object';
  }

  if (!SUPPORTED_ENTITY_TYPES.has(item.type)) {
    return `Unsupported type: ${String(item.type)}`;
  }

  if (!SUPPORTED_ACTIONS.has(item.action)) {
    return `Unsupported action: ${String(item.action)}`;
  }

  const data = item.data;
  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    return 'data must be an object';
  }

  switch (item.type) {
    case 'source': {
      if (typeof data.id !== 'string' || data.id.length === 0) {
        return 'source requires a non-empty "id" string';
      }
      if (typeof data.title !== 'string' || data.title.length === 0) {
        return 'source requires a non-empty "title" string';
      }
      // data (binary payload) is optional; if present it must be base64
      if (data.data !== undefined && data.data !== null && typeof data.data !== 'string') {
        return 'source "data" must be a base64-encoded string';
      }
      break;
    }
    case 'metadata': {
      if (typeof data.title !== 'string' || data.title.length === 0) {
        return 'metadata requires a non-empty "title" string';
      }
      if (typeof data.metadata_provider !== 'string' || data.metadata_provider.length === 0) {
        return 'metadata requires a non-empty "metadata_provider"';
      }
      if (typeof data.metadata_provider_key !== 'string' || data.metadata_provider_key.length === 0) {
        return 'metadata requires a non-empty "metadata_provider_key"';
      }
      if (typeof data.link_type !== 'number') {
        return 'metadata requires a numeric "link_type"';
      }
      break;
    }
  }

  return null;
}
