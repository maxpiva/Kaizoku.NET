import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { seriesService } from '@/lib/api/services/seriesService';
import { type FullSeries, type SeriesInfo, type SeriesExtendedInfo, type ProviderMatch, type AugmentedResponse, type LatestSeriesInfo, type SearchSource, type SeriesIntegrityResult } from '@/lib/api/types';

/**
 * Hook to get available search sources (for search and filtering)
 */
export const useSearchSources = () => {
  return useQuery({
    queryKey: ['search', 'sources'],
    queryFn: () => seriesService.getSearchSources(),
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};

/**
 * Hook to add series with full details to the library
 */
export const useAddSeries = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (augmentedResponse: AugmentedResponse) => seriesService.addSeries(augmentedResponse),
    onSuccess: () => {
      // Invalidate library query to refetch
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
    },
  });
};

/**
 * Hook to get library series (now returns SeriesInfo[])
 */
export const useLibrary = () => {
  return useQuery<SeriesInfo[]>({
    queryKey: ['series', 'library'],
    queryFn: () => seriesService.getLibrary(),
    staleTime: 30 * 1000, // 30 seconds - keep data fresh since series status changes affect tab counts
    refetchOnWindowFocus: true, // Refetch when user returns to the library page
  });
};

/**
 * Hook to get individual series by ID with extended information
 */
export const useSeriesById = (id: string, enabled = true) => {
  return useQuery<SeriesExtendedInfo>({
    queryKey: ['series', 'detail', id],
    queryFn: () => seriesService.getSeriesById(id),
    enabled: enabled && !!id,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};


/**
 * Hook to get provider match information by provider ID
 */
export const useProviderMatch = (providerId: string, enabled = true) => {
  return useQuery<ProviderMatch | null>({
    queryKey: ['series', 'match', providerId],
    queryFn: () => seriesService.getMatch(providerId),
    enabled: enabled && !!providerId,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });
};

/**
 * Hook to set provider match information
 */
export const useSetProviderMatch = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (providerMatch: ProviderMatch) => seriesService.setMatch(providerMatch),
    onSuccess: (_, variables) => {
      // Invalidate the specific provider match query
      void queryClient.invalidateQueries({ 
        queryKey: ['series', 'match', variables.id] 
      });
      // Also invalidate series details which might contain match information
      void queryClient.invalidateQueries({ 
        queryKey: ['series', 'detail'] 
      });
    },
  });
};

/**
 * Hook to get latest series from cloud providers
 * @param start Starting index for pagination
 * @param count Number of items to return
 * @param sourceId Optional source ID filter
 * @param keyword Optional keyword filter
 * @param enabled Whether the query should be enabled
 */
export const useLatest = (
  start: number, 
  count: number, 
  sourceId?: string, 
  keyword?: string, 
  enabled = true
) => {
  return useQuery<LatestSeriesInfo[]>({
    queryKey: ['series', 'latest', start, count, sourceId, keyword],
    queryFn: () => seriesService.getLatest(start, count, sourceId, keyword),
    enabled,
    staleTime: 2 * 60 * 1000, // 2 minutes - latest content changes frequently
    refetchInterval: 5 * 60 * 1000, // Auto-refetch every 5 minutes for fresh content
  });
};

/**
 * Hook to update series information
 */
export const useUpdateSeries = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (seriesData: SeriesExtendedInfo) => seriesService.updateSeries(seriesData),
    onSuccess: (updatedSeries) => {
      // Update the specific series in the cache
      queryClient.setQueryData(['series', 'detail', updatedSeries.id], updatedSeries);
      // Invalidate library query to refetch
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
    },
  });
};

/**
 * Hook to delete series from the library
 */
export const useDeleteSeries = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ id, alsoPhysical }: { id: string; alsoPhysical: boolean }) => 
      seriesService.deleteSeries(id, alsoPhysical),
    onSuccess: (_, variables) => {
      // Remove the specific series from the cache
      queryClient.removeQueries({ queryKey: ['series', 'detail', variables.id] });
      // Invalidate library query to refetch
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
    },
  });
};

/**
 * Hook to verify series integrity
 */
export const useVerifyIntegrity = () => {
  return useMutation({
    mutationFn: (id: string) => seriesService.verifyIntegrity(id),
  });
};

/**
 * Hook to cleanup series files with integrity issues
 */
export const useCleanupSeries = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (id: string) => seriesService.cleanupSeries(id),
    onSuccess: (_, id) => {
      // Invalidate the specific series detail to refetch updated data
      void queryClient.invalidateQueries({ queryKey: ['series', 'detail', id] });
      // Also invalidate library query in case this affects the series list
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
    },
  });
};

/**
 * Hook to update all series naming, filenames and ComicInfo.xml
 */
export const useUpdateAllSeries = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: () => seriesService.updateAllSeries(),
    onSuccess: () => {
      // Invalidate library query to refetch updated data
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
    },
  });
};
