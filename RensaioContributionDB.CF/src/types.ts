/// <reference types="@cloudflare/workers-types" />

/**
 * Cloudflare Worker environment bindings.
 */
export interface Env {
  // D1 database binding
  DB: D1Database;

  // GitHub API token (sensitive — set via: wrangler secret put GITHUB_TOKEN)
  GITHUB_TOKEN: string;

  // Export target repository, e.g. "owner/repo-name"
  EXPORT_GITHUB_REPO: string;

  // Export target path within the repository, e.g. "data/"
  EXPORT_PATH: string;

  // AES-256-CBC key and IV concatenated, base64-encoded (32B key + 16B IV = 64 base64 chars)
  // Used for obfuscation of source data in exports (not security).
  AESKEY256IV: string;
}

/**
 * Entity types that can be uploaded.
 * Titles are resolved server-side from source/metadata `title` strings.
 */
export type EntityType = 'source' | 'metadata';

/**
 * Actions that can be performed on an entity.
 * `add` is an upsert: insert if missing, update if the values differ,
 * skip if identical. `remove` soft-deletes by identity key.
 */
export type Action = 'add' | 'remove';

/**
 * All supported entity types for validation.
 */
export const SUPPORTED_ENTITY_TYPES: ReadonlySet<string> = new Set(['source', 'metadata']);

/**
 * All supported actions for validation.
 */
export const SUPPORTED_ACTIONS: ReadonlySet<string> = new Set(['add', 'remove']);

/**
 * Number of days after which archived records are hard-deleted by the daily scrub.
 */
export const ARCHIVE_RETENTION_DAYS = 30;
