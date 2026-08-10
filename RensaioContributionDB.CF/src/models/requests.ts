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
 * `id` is the contributor-provided identifier (e.g. "123456").
 * `title` is resolved server-side to a `title_id`.
 * `data` is a base64-encoded binary payload.
 */
export interface SourcePayload {
  id: string;
  title: string;
  data?: string | null;     // binary payload, base64-encoded
}

/**
 * Base shape for a metadata record payload.
 * No `id` — records are identified by identity key (title + provider + provider_key).
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
