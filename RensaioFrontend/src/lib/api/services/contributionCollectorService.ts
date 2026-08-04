import { apiClient } from "@/lib/api/client";
import {
  type ContributionCollectorStatus,
  type ContributionContributorValidation,
  type ContributionUploadStatus,
} from "@/lib/api/types";

export const contributionCollectorService = {
  async getStatus(): Promise<ContributionCollectorStatus> {
    return apiClient.get<ContributionCollectorStatus>(
      "/api/contributions/status",
    );
  },

  async runNow(): Promise<ContributionCollectorStatus> {
    // POST /api/contributions/run returns the full status DTO with HTTP 202.
    return apiClient.post<ContributionCollectorStatus>(
      "/api/contributions/run",
    );
  },

  async runUpload(): Promise<ContributionUploadStatus> {
    // POST /api/contributions/upload/run returns the upload status DTO with HTTP 202.
    return apiClient.post<ContributionUploadStatus>(
      "/api/contributions/upload/run",
    );
  },

  async validateContributor(): Promise<ContributionContributorValidation> {
    // Live-checks the stored contributor UUID against the contribution worker.
    return apiClient.post<ContributionContributorValidation>(
      "/api/contributions/upload/validate",
    );
  },
};
