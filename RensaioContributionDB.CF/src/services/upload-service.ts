import type { UploadItem } from '../models/requests';
import type { UploadError, UploadResponse } from '../models/responses';
import { SUPPORTED_ACTIONS, SUPPORTED_ENTITY_TYPES } from '../types';
import { base64ToBlob } from '../utils/binary';

/**
 * Process a batch of upload items for a single contributor.
 *
 * Titles are NOT uploaded directly. Sources and metadata carry a `title`
 * string, resolved server-side (reuse existing active title by name, or
 * create a new one). Title lookups are cached per request.
 *
 * Records are identified by their identity key, never by UUID:
 *   - source:  title + mihon_source_id + language
 *   - metadata: title + metadata_provider + metadata_provider_key
 *
 * Behaviors:
 *   - `add` is deduplicated: identical active record → skipped.
 *   - `update` matches ANY active record by identity key — regardless of
 *     which contributor created it — updates it, and transfers ownership
 *     (contributor_id) to the calling contributor.
 *   - `remove` soft-deletes ANY active record by identity key — regardless
 *     of which contributor created it.
 *   - Item-level validation errors are collected and returned.
 *   - All valid statements are executed in a single D1 batch (atomic).
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
      if (item.action === 'add' && (await existsDuplicate(db, item, titleCache))) {
        skipped += 1;
        continue;
      }

      const stmt = await buildStatement(db, item, contributorId, now, titleCache);
      statements.push(stmt);
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
 *
 * Reuses an existing ACTIVE title with the same name, or creates a new title
 * record. Results are cached per request in `titleCache`. New titles are
 * inserted immediately so their id is available to referencing statements.
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
 * Check whether an identical ACTIVE record already exists for an `add` item.
 *
 * The comparison uses the identity key and ignores `last_chapter`,
 * `last_change`, `archived_at` and `contributor_id` — matching exactly what
 * the contributor uploads. Archived records are never considered duplicates.
 */
async function existsDuplicate(
  db: D1Database,
  item: UploadItem,
  titleCache: Map<string, string>
): Promise<boolean> {
  const { type, data } = item;

  switch (type) {
    case 'source': {
      const titleId = await resolveTitle(db, data.title as string, titleCache);
      const existing = await db
        .prepare(
          `SELECT 1 FROM sources
           WHERE title_id = ? AND mihon_source_id = ? AND language = ?
             AND archived_at IS NULL
           LIMIT 1`
        )
        .bind(
          titleId,
          data.mihon_source_id,
          data.language
        )
        .first();
      return existing !== null;
    }
    case 'metadata': {
      const titleId = await resolveTitle(db, data.title as string, titleCache);
      const existing = await db
        .prepare(
          `SELECT 1 FROM metadata
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ? AND link_type = ?
             AND archived_at IS NULL
           LIMIT 1`
        )
        .bind(
          titleId,
          data.metadata_provider,
          data.metadata_provider_key,
          data.link_type
        )
        .first();
      return existing !== null;
    }
    default:
      return false;
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
      if (typeof data.title !== 'string' || data.title.length === 0) {
        return 'source requires a non-empty "title" string';
      }
      if (typeof data.mihon_source_id !== 'string' || data.mihon_source_id.length === 0) {
        return 'source requires a non-empty "mihon_source_id"';
      }
      if (typeof data.language !== 'string' || data.language.length === 0) {
        return 'source requires a non-empty "language"';
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

/**
 * Build a prepared statement for one item. Resolves the title id via the
 * per-request title cache for source/metadata items.
 */
async function buildStatement(
  db: D1Database,
  item: UploadItem,
  contributorId: string,
  now: string,
  titleCache: Map<string, string>
): Promise<D1PreparedStatement> {
  const { type, action, data } = item;
  // BLOB field handling: the JSON body carries base64; decode to binary.
  const dataBlob = base64ToBlob(data.data);

  // Resolve the title id for source/metadata items.
  const titleId = await resolveTitle(db, data.title as string, titleCache);

  switch (action) {
    case 'add':
      return buildAddStatement(db, type, data, dataBlob, titleId, contributorId, now);
    case 'update':
      return buildUpdateStatement(db, type, data, dataBlob, titleId, contributorId, now);
    case 'remove':
      return buildRemoveStatement(db, type, titleId, now);
    default:
      throw new Error(`Unsupported action: ${String(action)}`);
  }
}

/**
 * INSERT a new record (id is always generated server-side).
 */
function buildAddStatement(
  db: D1Database,
  type: string,
  data: Record<string, unknown>,
  dataBlob: ArrayBuffer | null,
  titleId: string,
  contributorId: string,
  now: string
): D1PreparedStatement {
  const id = crypto.randomUUID();

  switch (type) {
    case 'source':
      return db
        .prepare(
          `INSERT INTO sources (id, title_id, mihon_source_id, language, last_chapter, data, contributor_id, last_change, archived_at)
           VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL)`
        )
        .bind(
          id,
          titleId,
          data.mihon_source_id,
          data.language,
          data.last_chapter ?? null,
          dataBlob,
          contributorId,
          now
        );
    case 'metadata':
      return db
        .prepare(
          `INSERT INTO metadata (id, title_id, metadata_provider, metadata_provider_key, link_type, contributor_id, last_change, archived_at)
           VALUES (?, ?, ?, ?, ?, ?, ?, NULL)`
        )
        .bind(
          id,
          titleId,
          data.metadata_provider,
          data.metadata_provider_key,
          data.link_type,
          contributorId,
          now
        );
    default:
      throw new Error(`Unsupported type: ${type}`);
  }
}

/**
 * UPDATE any existing active record matched by identity key — regardless of
 * which contributor created it. The calling contributor becomes the new
 * owner (contributor_id is reassigned) and last_change is refreshed.
 */
function buildUpdateStatement(
  db: D1Database,
  type: string,
  data: Record<string, unknown>,
  dataBlob: ArrayBuffer | null,
  titleId: string,
  contributorId: string,
  now: string
): D1PreparedStatement {
  switch (type) {
    case 'source':
      return db
        .prepare(
          `UPDATE sources
           SET last_chapter = ?, data = ?, contributor_id = ?, last_change = ?
           WHERE title_id = ? AND mihon_source_id = ? AND language = ?
             AND archived_at IS NULL`
        )
        .bind(
          data.last_chapter ?? null,
          dataBlob,
          contributorId,
          now,
          titleId,
          data.mihon_source_id,
          data.language
        );
    case 'metadata':
      return db
        .prepare(
          `UPDATE metadata
           SET link_type = ?, contributor_id = ?, last_change = ?
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ?
             AND archived_at IS NULL`
        )
        .bind(
          data.link_type,
          contributorId,
          now,
          titleId,
          data.metadata_provider,
          data.metadata_provider_key
        );
    default:
      throw new Error(`Unsupported type: ${type}`);
  }
}

/**
 * Soft-delete ANY existing active record matched by identity key — regardless
 * of which contributor created it.
 */
function buildRemoveStatement(
  db: D1Database,
  type: string,
  titleId: string,
  now: string
): D1PreparedStatement {
  switch (type) {
    case 'source':
      return db
        .prepare(
          `UPDATE sources
           SET archived_at = ?
           WHERE title_id = ? AND mihon_source_id = ? AND language = ?
             AND archived_at IS NULL`
        )
        .bind(now, titleId);
    case 'metadata':
      return db
        .prepare(
          `UPDATE metadata
           SET archived_at = ?
           WHERE title_id = ? AND metadata_provider = ? AND metadata_provider_key = ?
             AND archived_at IS NULL`
        )
        .bind(now, titleId);
    default:
      throw new Error(`Unsupported type: ${type}`);
  }
}
