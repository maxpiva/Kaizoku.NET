import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { settingsService } from "@/lib/api/services/settingsService";
import { contributionCollectorKeys } from "@/lib/api/hooks/useContributionCollector";
import { type Settings } from "@/lib/api/types";

export const useSettings = () => {
  return useQuery({
    queryKey: ["settings"],
    queryFn: () => settingsService.getSettings(),
  });
};

export const useAvailableLanguages = () => {
  return useQuery({
    queryKey: ["settings", "languages"],
    queryFn: () => settingsService.getAvailableLanguages(),
  });
};

export const useUpdateSettings = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (settings: Settings) =>
      settingsService.updateSettings(settings),
    onSuccess: (data) => {
      // If the backend returned a set-password URL, the settings-manager
      // will handle the redirect. Otherwise, invalidate settings.
      if (!data?.setPasswordUrl) {
        queryClient.invalidateQueries({ queryKey: ["settings"] });
        queryClient.invalidateQueries({
          queryKey: contributionCollectorKeys.status,
        });
      }
    },
  });
};
