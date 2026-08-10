import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { contributionCollectorService } from "@/lib/api/services/contributionCollectorService";

// States during which the backend has an active or imminent run: poll fast.
const ACTIVE_COLLECTOR_STATES = new Set(["queued", "running", "yielding"]);

const isCollectorActive = (state?: string | null) =>
  ACTIVE_COLLECTOR_STATES.has((state ?? "").toLowerCase());

export const contributionCollectorKeys = {
  all: ["contributions"] as const,
  status: ["contributions", "status"] as const,
};

export function useContributionCollectorStatus(enabled: boolean) {
  return useQuery({
    queryKey: contributionCollectorKeys.status,
    queryFn: () => contributionCollectorService.getStatus(),
    enabled,
    refetchOnWindowFocus: true,
    refetchInterval: (query) => {
      if (!enabled) return false;
      const active =
        isCollectorActive(query.state.data?.state) ||
        isCollectorActive(query.state.data?.upload?.state) ||
        isCollectorActive(query.state.data?.snapshot?.state);
      return active ? 5_000 : 30_000;
    },
  });
}

export function useRunContributionCollector() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => contributionCollectorService.runNow(),
    onSuccess: (status) => {
      // The run endpoint returns the full status DTO; seed the cache with it so the
      // UI reflects the queued state immediately, then refetch for freshness.
      queryClient.setQueryData(contributionCollectorKeys.status, status);
      void queryClient.invalidateQueries({
        queryKey: contributionCollectorKeys.status,
      });
    },
  });
}

export function useRunContributionUpload() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => contributionCollectorService.runUpload(),
    onSuccess: () => {
      // The run endpoint returns only the upload half; refetch the combined status.
      void queryClient.invalidateQueries({
        queryKey: contributionCollectorKeys.status,
      });
    },
  });
}

export function useRunContributionSnapshot() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => contributionCollectorService.runSnapshot(),
    onSuccess: () => {
      // The run endpoint returns only the snapshot half; refetch the combined status.
      void queryClient.invalidateQueries({
        queryKey: contributionCollectorKeys.status,
      });
    },
  });
}

export function useValidateContributor() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => contributionCollectorService.validateContributor(),
    onSuccess: () => {
      // Validation results are persisted server-side into the upload status.
      void queryClient.invalidateQueries({
        queryKey: contributionCollectorKeys.status,
      });
    },
  });
}
