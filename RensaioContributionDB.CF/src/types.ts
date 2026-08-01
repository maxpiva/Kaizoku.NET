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
}

/**
 * Entity types that can be uploaded.
 *
 * Titles are not uploaded directly — sources and metadata carry a `title`
 * string, and the worker resolves/reuses/creates title records server-side.
 */
export type EntityType = 'source' | 'metadata';

/**
 * Actions that can be performed on an entity.
 */
export type Action = 'add' | 'update' | 'remove';

/**
 * All supported entity types for validation.
 */
export const SUPPORTED_ENTITY_TYPES: ReadonlySet<string> = new Set(['source', 'metadata']);

/**
 * All supported actions for validation.
 */
export const SUPPORTED_ACTIONS: ReadonlySet<string> = new Set(['add', 'update', 'remove']);

/**
 * Number of days after which archived records are hard-deleted by the daily scrub.
 */
export const ARCHIVE_RETENTION_DAYS = 30;
