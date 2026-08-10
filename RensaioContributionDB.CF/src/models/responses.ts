/**
 * Error response body used across all endpoints.
 */
export interface ErrorResponse {
  error: string;
}

/**
 * Response for GET /contributor.
 */
export interface ContributorResponse {
  active: boolean;
  admin: boolean;
  ban_reason: string | null;
}

/**
 * Response for POST /contributor.
 */
export interface CreateContributorResponse {
  contributor_id: string;
}

/**
 * A single item-level error from a batch upload.
 */
export interface UploadError {
  index: number;
  message: string;
}

/**
 * Response for POST /upload.
 */
export interface UploadResponse {
  processed: number;
  skipped: number;
  errors: UploadError[];
}

/**
 * Summary of records scrubbed when banning a contributor.
 */
export interface BanScrubSummary {
  sources: number;
  metadata: number;
}

/**
 * Response for POST /admin/ban.
 */
export interface BanResponse {
  banned: boolean;
  contributor_id: string;
  ban_reason: string;
  scrubbed: BanScrubSummary;
}

/**
 * Response for the daily export job.
 */
export interface ExportResponse {
  exported: boolean;
  files: string[];
  scrubbed: {
    sources: number;
    metadata: number;
    titles: number;
  };
}
