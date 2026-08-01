import type { Env } from '../types';
import { ARCHIVE_RETENTION_DAYS } from '../types';
import type { Metadata, Source, Title } from '../db/schema';
import { blobToBase64 } from '../utils/binary';

/**
 * Run the daily job:
 *  1. Scrub archived records older than the retention window (hard-delete).
 *  2. Export the latest non-archived state to the GitHub repository
 *     as sources.json, metadata.json and titles.json (no contributor_id).
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
      'SELECT id, title_id, mihon_source_id, language, last_chapter, data FROM sources WHERE archived_at IS NULL'
    ).all<Source>(),
    env.DB.prepare(
      'SELECT id, title_id, metadata_provider, metadata_provider_key, link_type FROM metadata WHERE archived_at IS NULL'
    ).all<Metadata>(),
    env.DB.prepare('SELECT id, title FROM titles WHERE archived_at IS NULL').all<Title>(),
  ]);

  // Serialize source rows for the JSON export.
  // `title_id` is kept as-is (compact reference to titles.json), `data` is
  // base64-encoded, and internal columns (`id`, `contributor_id`) are excluded.
  // Key names are shortened: source_id, last.
  const serializedSources = sources.results.map((source) => ({
    title_id: source.title_id,
    source_id: source.mihon_source_id,
    language: source.language,
    last: source.last_chapter,
    data: source.data ? blobToBase64(source.data) : null,
  }));

  // Same for metadata: `title_id` is kept, no `id`/`contributor_id`.
  // Key names are shortened: provider, provider_key, type.
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
 * deleted first; titles are deleted last. Only titles whose archived_at has
 * passed the threshold are removed, and any child records referencing them
 * were already removed in this same pass.
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
 *
 * If the file already exists, its sha is fetched first so the update can
 * be applied. If it does not exist yet, the file is created.
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
    content: btoa(content),
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
