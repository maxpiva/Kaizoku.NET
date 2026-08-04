import type { Env } from '../types';
import { ARCHIVE_RETENTION_DAYS } from '../types';
import type { Metadata, Source, Title } from '../db/schema';
import { encryptSourceData } from '../utils/crypto';

/**
 * Run the daily job:
 *  1. Scrub archived records older than the retention window (hard-delete).
 *  2. Export the latest non-archived state to the GitHub repository
 *     as sources.json, metadata.json and titles.json.
 *
 * Sources data is obfuscated for export:
 *   BLOB → AES-256-CBC encrypt → base64
 *
 * @returns a summary of what was scrubbed and which files were exported.
 */
export async function runDailyExport(env: Env): Promise<{
  scrubbed: { sources: number; metadata: number; titles: number };
  files: string[];
  exported: boolean;
}> {
  const scrubbed = await scrubArchived(env.DB);

  const [sources, metadata, titles] = await Promise.all([
    env.DB.prepare(
      'SELECT id, title_id, data FROM sources WHERE archived_at IS NULL'
    ).all<Source>(),
    env.DB.prepare(
      'SELECT title_id, metadata_provider, metadata_provider_key, link_type FROM metadata WHERE archived_at IS NULL'
    ).all<Metadata>(),
    env.DB.prepare('SELECT id, title FROM titles WHERE archived_at IS NULL').all<Title>(),
  ]);

  // Serialize source rows for the JSON export.
  // `data` goes through the obfuscation pipeline; internal columns
  // (`contributor_id`) are excluded.
  const serializedSources = [];
  for (const source of sources.results) {
    serializedSources.push({
      title_id: source.title_id,
      id: source.id,
      data: source.data ? await encryptSourceData(source.data, env.AESKEY256IV) : null,
    });
  }

  // Metadata keeps its compact key names: provider, provider_key, type.
  const serializedMetadata = metadata.results.map((meta) => ({
    title_id: meta.title_id,
    provider: meta.metadata_provider,
    provider_key: meta.metadata_provider_key,
    type: meta.link_type,
  }));

  const files = [
    { name: 'sources.json', content: JSON.stringify(serializedSources, null, 2) },
    { name: 'metadata.json', content: JSON.stringify(serializedMetadata, null, 2) },
    { name: 'titles.json', content: JSON.stringify(titles.results, null, 2) },
  ];

  const pushed: string[] = [];
  for (const file of files) {
    await pushFileToGitHub(env, file.name, file.content);
    pushed.push(file.name);
  }

  return { scrubbed, files: pushed, exported: true };
}

/**
 * Permanently delete records soft-deleted more than ARCHIVE_RETENTION_DAYS ago.
 *
 * Ordering matters: sources and metadata (which hold FKs to titles) are
 * deleted first; titles are deleted last.
 */
export async function scrubArchived(db: D1Database): Promise<{
  sources: number;
  metadata: number;
  titles: number;
}> {
  const sources = await db
    .prepare(
      `DELETE FROM sources
       WHERE archived_at IS NOT NULL
         AND archived_at < datetime('now', ?)`
    )
    .bind(`-${ARCHIVE_RETENTION_DAYS} days`)
    .run();

  const metadata = await db
    .prepare(
      `DELETE FROM metadata
       WHERE archived_at IS NOT NULL
         AND archived_at < datetime('now', ?)`
    )
    .bind(`-${ARCHIVE_RETENTION_DAYS} days`)
    .run();

  const titles = await db
    .prepare(
      `DELETE FROM titles
       WHERE archived_at IS NOT NULL
         AND archived_at < datetime('now', ?)`
    )
    .bind(`-${ARCHIVE_RETENTION_DAYS} days`)
    .run();

  return {
    sources: sources.meta.changes,
    metadata: metadata.meta.changes,
    titles: titles.meta.changes,
  };
}

/**
 * Push a file to the target GitHub repository via the Contents API.
 */
async function pushFileToGitHub(env: Env, fileName: string, content: string): Promise<void> {
  const repo = env.EXPORT_GITHUB_REPO;
  const path = `${env.EXPORT_PATH.replace(/\/$/, '')}/${fileName}`;
  const api = `https://api.github.com/repos/${repo}/contents/${path}`;

  // Get the current sha if the file exists
  const existing = await fetch(api, {
    headers: {
      Authorization: `Bearer ${env.GITHUB_TOKEN}`,
      Accept: 'application/vnd.github+json',
      'User-Agent': 'rensaio-contribution-db',
    },
  });

  let sha: string | undefined;
  if (existing.ok) {
    const body = (await existing.json()) as { sha?: string };
    sha = body.sha;
  }

  const body = {
    message: `Daily contribution export: ${fileName}`,
    content: toBase64(content),
    ...(sha ? { sha } : {}),
  };

  const response = await fetch(api, {
    method: 'PUT',
    headers: {
      Authorization: `Bearer ${env.GITHUB_TOKEN}`,
      Accept: 'application/vnd.github+json',
      'Content-Type': 'application/json',
      'User-Agent': 'rensaio-contribution-db',
    },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const detail = await response.text();
    throw new Error(`GitHub push failed for ${fileName}: ${response.status} ${detail}`);
  }
}

/**
 * UTF-8 safe base64 encoding.
 *
 * `btoa` only handles Latin-1 and throws on non-ASCII characters (e.g.
 * Japanese/Korean manga titles). Encoding to UTF-8 bytes first makes it
 * safe for any content.
 */
function toBase64(input: string): string {
  const bytes = new TextEncoder().encode(input);
  let binary = '';
  const chunkSize = 0x8000;
  for (let i = 0; i < bytes.length; i += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunkSize));
  }
  return btoa(binary);
}
