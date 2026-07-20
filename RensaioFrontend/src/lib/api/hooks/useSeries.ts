import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { seriesService } from '@/lib/api/services/seriesService';
import { type FullSeries, type SeriesInfo, type SeriesExtendedInfo, type ProviderMatch, type AugmentedResponse, type LatestSeriesInfo, type LatestGenre, type SearchSource, type SeriesIntegrityResult, type SeriesRenameResult, type ChapterDetail } from '@/lib/api/types';

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
 * @param genres Optional tag/genre filter; a row must carry every supplied tag (AND semantics)
 * @param enabled Whether the query should be enabled
 */
export const useLatest = (
  start: number,
  count: number,
  sourceId?: string,
  keyword?: string,
  genres?: string[],
  enabled = true
) => {
  // Normalize the genre filter into a stable cache-key fragment so identical
  // selections (order/case/whitespace aside) share a cache entry.
  const genreKey = (genres ?? [])
    .map((g) => g.trim().toLowerCase())
    .filter((g) => g.length > 0)
    .sort();

  return useQuery<LatestSeriesInfo[]>({
    queryKey: ['series', 'latest', start, count, sourceId, keyword, genreKey],
    queryFn: () => seriesService.getLatest(start, count, sourceId, keyword, genres),
    enabled,
    staleTime: 2 * 60 * 1000, // 2 minutes - latest content changes frequently
    refetchInterval: 5 * 60 * 1000, // Auto-refetch every 5 minutes for fresh content
  });
};

/**
 * Hook to get the distinct tags/genres available in the cached "Latest" cloud
 * catalogue, each with its occurrence count. Backing data for the browse tag filter.
 */
export const useLatestGenres = () => {
  return useQuery<LatestGenre[]>({
    queryKey: ['series', 'latest', 'genres'],
    queryFn: () => seriesService.getLatestGenres(),
    staleTime: 5 * 60 * 1000, // 5 minutes
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
 * Hook to set the release cadence for a series (user override).
 * Invalidates both the series detail and status queries.
 */
export const useSetSeriesCadence = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ seriesId, cadenceDays }: { seriesId: string; cadenceDays: number | null }) =>
      seriesService.setCadence(seriesId, cadenceDays),
    onSuccess: (_, { seriesId }) => {
      void queryClient.invalidateQueries({ queryKey: ['series', 'detail', seriesId] });
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
      void queryClient.invalidateQueries({ queryKey: ['status'] });
    },
  });
};

/**
 * Hook to trigger an immediate metadata + new-chapter refresh for a single series
 */
export const useRefreshSeries = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => seriesService.refreshSeries(id),
    onSuccess: (_, id) => {
      // The refresh runs asynchronously on the backend; invalidate so the detail
      // (and library tab counts) pick up new metadata/chapters once jobs complete.
      void queryClient.invalidateQueries({ queryKey: ['series', 'detail', id] });
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
    },
  });
};

/**
 * Hook to rename a series folder + its .cbz archives to the canonical naming scheme.
 */
export const useRenameSeries = () => {
  const queryClient = useQueryClient();

  return useMutation<SeriesRenameResult, Error, string>({
    mutationFn: (id: string) => seriesService.renameSeries(id),
    onSuccess: (_, id) => {
      // Storage path / filenames changed — refetch the detail (and library counts).
      void queryClient.invalidateQueries({ queryKey: ['series', 'detail', id] });
      void queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
    },
  });
};

/**
 * Hook to get the unified, series-level chapter list (merged across every source).
 * Lazy: pass `enabled` so it only fetches when the Chapters section is expanded.
 */
export const useSeriesChapters = (seriesId: string, enabled = true) => {
  return useQuery<ChapterDetail[]>({
    queryKey: ['series', 'chapters', seriesId],
    queryFn: () => seriesService.getSeriesChapters(seriesId),
    enabled: enabled && !!seriesId,
    staleTime: 60 * 1000, // 1 minute
  });
};

/**
 * Hook to re-download (or download) a single chapter. Omit `providerId` for the priority default
 * source; pass it to force a specific source. On success the chapters + detail queries are
 * invalidated so attribution/missing state refreshes once the download job completes.
 */
export const useRedownloadChapter = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ seriesId, chapterNumber, providerId }: { seriesId: string; chapterNumber: number; providerId?: string }) =>
      seriesService.redownloadChapter(seriesId, chapterNumber, providerId),
    onSuccess: (_, variables) => {
      void queryClient.invalidateQueries({ queryKey: ['series', 'chapters', variables.seriesId] });
      void queryClient.invalidateQueries({ queryKey: ['series', 'detail', variables.seriesId] });
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
