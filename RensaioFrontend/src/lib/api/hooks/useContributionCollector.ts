import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { contributionCollectorService } from "@/lib/api/services/contributionCollectorService";

const isCollectorRunning = (state?: string | null) =>
  (state ?? "").toLowerCase() === "running";

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
      return isCollectorRunning(query.state.data?.state) ? 5_000 : 30_000;
    },
  });
}

export function useRunContributionCollector() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => contributionCollectorService.runNow(),
    onSuccess: () => {
      void queryClient.invalidateQueries({
        queryKey: contributionCollectorKeys.status,
      });
    },
  });
}
