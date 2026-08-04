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
 *     No content comparison — the latest upload always wins.
 *   - `remove` — soft-delete ANY active record by identity key.
 *
 * Identity keys:
 *   - source:  `id` (contributor-provided identifier, DB PK)
 *   - metadata: title + metadata_provider + metadata_provider_key
 *
 * Titles are resolved server-side (reuse active title by name, or create).
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

  for (let index = 0; index < items.length; index += 1) {
    const item = items[index];

    const error = validateItem(item);
    if (error) {
      errors.push({ index, message: error });
      continue;
    }

    try {
      if (item.action === 'add') {
        statements.push(await buildUpsertStatement(db, item, contributorId, now, titleCache));
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

  return { processed: statements.length, skipped: 0, errors };
}

/**
 * Resolve a title name to a title id.
 * Reuses an existing ACTIVE title with the same name, or creates a new title
 * record. Results are cached per request.
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

  // Create a new title
  const id = crypto.randomUUID();
  await db
    .prepare('INSERT INTO titles (id, title, archived_at) VALUES (?, ?, NULL)')
    .bind(id, title, null)
    .run();

  titleCache.set(title, id);
  return id;
}

/**
 * Build the upsert statement for an `add` item.
 *
 * IMPORTANT: `id` is the PRIMARY KEY for sources, so the existence check
 * matches REGARDLESS of `archived_at`. If the row exists (active or
 * archived), we UPDATE and clear `archived_at` (resurrect) — otherwise the
 * INSERT would violate the primary-key constraint. This means we never end
 * up with duplicate PKs or a dead archived row shadowing the key.
 */
async function buildUpsertStatement(
  db: D1Database,
  item: UploadItem,
  contributorId: string,
  now: string,
  titleCache: Map<string, string>
): Promise<D1PreparedStatement> {
  const { type, data } = item;
  const dataBlob = base64ToBlob(data.data);
  const titleId = await resolveTitle(db, data.title as string, titleCache);

  switch (type) {
    case 'source': {
      const existing = await db
        .prepare('SELECT 1 FROM sources WHERE id = ? LIMIT 1')
        .bind(data.id)
        .first();

      if (!existing) {
        // insert
        return db
          .prepare(
            `INSERT INTO sources (id, title_id, data, contributor_id, last_change, archived_at)
             VALUES (?, ?, ?, ?, ?, NULL)`
          )
          .bind(data.id, titleId, dataBlob, contributorId, now);
      }

      // update + ownership transfer + clear archived_at (resurrect if archived)
      return db
        .prepare(
          `UPDATE sources
           SET title_id = ?, data = ?, contributor_id = ?, last_change = ?, archived_at = NULL
           WHERE id = ?`
        )
        .bind(titleId, dataBlob, contributorId, now, data.id);
    }
    case 'metadata': {
      const existing = await db
        .prepare(
          `SELECT 1 FROM metadata
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ?
           LIMIT 1`
        )
        .bind(titleId, data.metadata_provider, data.metadata_provider_key)
        .first();

      if (!existing) {
        // insert
        return db
          .prepare(
            `INSERT INTO metadata (id, title_id, metadata_provider, metadata_provider_key, link_type, contributor_id, last_change, archived_at)
             VALUES (?, ?, ?, ?, ?, ?, ?, NULL)`
          )
          .bind(
            crypto.randomUUID(),
            titleId,
            data.metadata_provider,
            data.metadata_provider_key,
            data.link_type,
            contributorId,
            now
          );
      }

      // update + ownership transfer + clear archived_at (resurrect if archived)
      return db
        .prepare(
          `UPDATE metadata
           SET link_type = ?, contributor_id = ?, last_change = ?, archived_at = NULL
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ?`
        )
        .bind(
          data.link_type,
          contributorId,
          now,
          titleId,
          data.metadata_provider,
          data.metadata_provider_key
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
