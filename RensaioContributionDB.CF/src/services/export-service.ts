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
  orphanTitlesArchived: number;
  files: string[];
  exported: boolean;
}> {
  const scrubbed = await scrubArchived(env.DB);

  // Titles are never archived by removes/scrubs; archive the ones with no
  // active references so titles.json doesn't grow unboundedly.
  const orphanTitlesArchived = await archiveOrphanTitles(env.DB);

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

  return { scrubbed, orphanTitlesArchived, files: pushed, exported: true };
}

/**
 * Archive titles that no longer have any active references.
 *
 * Removes/scrubs never touch titles, so titles.json used to grow
 * monotonically even when every source/metadata referencing a title was
 * removed. Running this daily (before export) keeps titles.json bounded —
 * orphaned titles are soft-archived here and hard-deleted by the next
 * retention scrub 30 days later.
 */
export async function archiveOrphanTitles(db: D1Database): Promise<number> {
  const now = new Date().toISOString();
  const result = await db
    .prepare(
      `UPDATE titles
       SET archived_at = ?
       WHERE archived_at IS NULL
         AND NOT EXISTS (
           SELECT 1 FROM sources WHERE sources.title_id = titles.id AND sources.archived_at IS NULL
         )
         AND NOT EXISTS (
           SELECT 1 FROM metadata WHERE metadata.title_id = titles.id AND metadata.archived_at IS NULL
         )`
    )
    .bind(now)
    .run();

  return result.meta.changes;
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
  const path = buildExportPath(env.EXPORT_PATH, fileName);
  const api = `https://api.github.com/repos/${repo}/contents/${path}`;
  const headers: Record<string, string> = {
    Authorization: `Bearer ${env.GITHUB_TOKEN}`,
    Accept: 'application/vnd.github+json',
    'User-Agent': 'rensaio-contribution-db',
  };

  // Get the current sha if the file exists. The plain Contents GET returns
  // 404 for blobs >1MB (sources.json crosses that quickly), so we fall back
  // to the git trees API, which reports the blob sha regardless of size.
  const sha = await getFileSha(env, path, headers);

  const body = {
    message: `Daily contribution export: ${fileName}`,
    content: toBase64(content),
    ...(sha ? { sha } : {}),
  };

  const response = await fetch(api, {
    method: 'PUT',
    headers: { ...headers, 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    const detail = await response.text();
    throw new Error(`GitHub push failed for ${fileName}: ${response.status} ${detail}`);
  }
}

/**
 * Resolve the current blob sha of a file in the export repository.
 *
 * GitHub's Contents API only serves blobs up to 1MB; for larger files it
 * responds 404 with a "This API returns blobs up to 1 MB in size" body, which
 * would make us believe the file doesn't exist and PUT without a sha →
 * 409/422. The git trees API reports the blob sha for files of any size.
 *
 * Returns undefined when the file does not exist (the PUT will create it).
 */
async function getFileSha(
  env: Env,
  path: string,
  headers: Record<string, string>
): Promise<string | undefined> {
  const repo = env.EXPORT_GITHUB_REPO;
  const api = `https://api.github.com/repos/${repo}/contents/${path}`;

  const existing = await fetch(api, { headers });
  if (existing.ok) {
    const body = (await existing.json()) as { sha?: string };
    return body.sha;
  }

  if (existing.status !== 404) {
    return undefined;
  }

  // The file may be >1MB (or genuinely absent). Resolve its sha via the git
  // trees API; absent files simply yield no entry.
  const commit = await fetch(`https://api.github.com/repos/${repo}/commits/HEAD`, { headers });
  if (!commit.ok) {
    return undefined;
  }
  const commitBody = (await commit.json()) as { commit?: { tree?: { sha?: string } } };
  const treeSha = commitBody.commit?.tree?.sha;
  if (!treeSha) {
    return undefined;
  }

  const tree = await fetch(
    `https://api.github.com/repos/${repo}/git/trees/${treeSha}?recursive=1`,
    { headers }
  );
  if (!tree.ok) {
    return undefined;
  }
  const treeBody = (await tree.json()) as { tree?: Array<{ path?: string; sha?: string }> };
  return treeBody.tree?.find((entry) => entry.path === path)?.sha;
}

/**
 * Join the export root and a file name into a repo-relative path.
 *
 * GitHub rejects paths that start with a slash ("path cannot start with a
 * slash"). A root of "/" or "" must produce a bare file name ("sources.json"),
 * and a root like "data/" must produce "data/sources.json".
 */
function buildExportPath(exportPath: string, fileName: string): string {
  const normalized = exportPath.replace(/^\/+|\/+$/g, '');
  return normalized ? `${normalized}/${fileName}` : fileName;
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
