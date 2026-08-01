import type { Action, EntityType } from '../types';

/**
 * A single upload item within a contribution batch.
 */
export interface UploadItem {
  type: EntityType;
  action: Action;
  data: Record<string, unknown>;
}

/**
 * Request body for POST /upload.
 */
export interface UploadRequest {
  items: UploadItem[];
}

/**
 * Request body for POST /admin/ban.
 */
export interface BanRequest {
  contributor_id: string;
  ban_reason: string;
}

/**
 * Base shape for a source record payload.
 *
 * The upload carries the `title` string — the worker resolves it to a
 * title_id server-side (reusing an existing active title or creating one).
 * No `id` is sent: `update`/`remove` match the record by identity key
 * (title + mihon_source_id + language) rather than by UUID.
 */
export interface SourcePayload {
  title: string;
  mihon_source_id: string;
  language: string;
  last_chapter?: string | null;
  data?: string | null;     // binary payload, base64-encoded
}

/**
 * Base shape for a metadata record payload.
 *
 * No `id` is sent: `update`/`remove` match the record by identity key
 * (title + metadata_provider + metadata_provider_key) rather than by UUID.
 */
export interface MetadataPayload {
  title: string;
  metadata_provider: string;
  metadata_provider_key: string;
  link_type: number;
}

/**
 * Request body for POST /contributor.
 */
export interface CreateContributorRequest {
  is_admin?: boolean;
}
