import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { settingsService } from '@/lib/api/services/settingsService';
import { type Settings } from '@/lib/api/types';

export const useSettings = () => {
  return useQuery({
    queryKey: ['settings'],
    queryFn: () => settingsService.getSettings(),
  });
};

export const useAvailableLanguages = () => {
  return useQuery({
    queryKey: ['settings', 'languages'],
    queryFn: () => settingsService.getAvailableLanguages(),
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
