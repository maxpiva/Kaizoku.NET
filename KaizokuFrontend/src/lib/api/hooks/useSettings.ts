import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { settingsService } from '@/lib/api/services/settingsService';
import { type Settings } from '@/lib/api/types';
import { useAuth } from '@/contexts/auth-context';

export const useSettings = () => {
  const { isAuthenticated } = useAuth();

  return useQuery({
    queryKey: ['settings'],
    queryFn: () => settingsService.getSettings(),
    // Don't fetch settings until the user is authenticated —
    // the endpoint requires RequireAdmin and will 401 otherwise,
    // triggering a token-refresh loop in the API client.
    enabled: isAuthenticated,
  });
};

export const useAvailableLanguages = () => {
  const { isAuthenticated } = useAuth();

  return useQuery({
    queryKey: ['settings', 'languages'],
    queryFn: () => settingsService.getAvailableLanguages(),
    enabled: isAuthenticated,
  });
};

export const useUpdateSettings = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (settings: Settings) => settingsService.updateSettings(settings),
    onSuccess: () => {
      // Invalidate settings query to refetch the updated data
      queryClient.invalidateQueries({ queryKey: ['settings'] });
    },
  });
};
