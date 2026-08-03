import { apiClient } from "@/lib/api/client";
import {
  type ContributionCollectorRunResponse,
  type ContributionCollectorStatus,
} from "@/lib/api/types";

export const contributionCollectorService = {
  async getStatus(): Promise<ContributionCollectorStatus> {
    return apiClient.get<ContributionCollectorStatus>(
      "/api/contributions/status",
    );
  },

  async runNow(): Promise<ContributionCollectorRunResponse | void> {
    return apiClient.post<ContributionCollectorRunResponse | void>(
      "/api/contributions/run",
    );
  },
};
