import { apiClient } from "@/lib/api/client";
import { type ContributionCollectorStatus } from "@/lib/api/types";

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
};
