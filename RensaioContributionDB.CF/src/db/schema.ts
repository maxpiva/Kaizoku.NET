/// <reference types="@cloudflare/workers-types" />

/**
 * Represents a row in the `contributors` D1 table.
 */
export interface Contributor {
  id: string;          // UUID
  admin: number;       // 0/1 — admin privileges
  active: number;      // 0/1 — 0 = banned
  ban_reason: string | null;
  last_change: string; // ISO 8601 UTC datetime
}

/**
 * Represents a row in the `titles` D1 table.
 */
export interface Title {
  id: string;          // UUID
  title: string;
  archived_at: string | null;
}

/**
 * Represents a row in the `sources` D1 table.
 * `id` is the contributor-provided identifier (e.g. "123456"), not a UUID.
 */
export interface Source {
  id: string;              // contributor-provided identifier
  title_id: string;        // FK → titles.id
  data: ArrayBuffer | null; // binary payload (BLOB column)
  contributor_id: string;  // FK → contributors.id
  last_change: string;     // ISO 8601 UTC datetime
  archived_at: string | null;
}

/**
 * Represents a row in the `metadata` D1 table.
 */
export interface Metadata {
  id: string;                    // UUID
  title_id: string;              // FK → titles.id
  metadata_provider: string;     // e.g. "anilist", "mal"
  metadata_provider_key: string; // provider-specific ID
  link_type: number;             // type of link
  contributor_id: string;        // FK → contributors.id
  last_change: string;           // ISO 8601 UTC datetime
  archived_at: string | null;
}
