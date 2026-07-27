import { useQuery, useMutation } from '@tanstack/react-query';
import { searchService, type SearchParams } from '@/lib/api/services/searchService';
import { type LinkedSeries } from '@/lib/api/types';

/**
 * Hook for getting available search sources
 */
export const useAvailableSearchSources = () => {
  return useQuery({
    queryKey: ['search', 'sources'],
    queryFn: () => searchService.getAvailableSearchSources(),
  });
};

/**
 * Hook for searching series across multiple sources
 * @param params Search parameters containing keyword, optional languages, and optional search sources
 * @param options React Query options
 */
export const useSearchSeries = (
  params: SearchParams,
  options?: {
    enabled?: boolean;
  }
) => {
  return useQuery({
    queryKey: ['search', 'series', params.keyword, params.languages, params.searchSources],
    queryFn: () => searchService.searchSeries(params),
    enabled: options?.enabled ?? !!params.keyword?.trim(),
  });
};

/**
 * Hook for augmenting linked series with full details
 * Returns the full AugmentedResponse with metadata and series
 */
export const useAugmentSeries = () => {
  return useMutation({
    mutationFn: (linkedSeries: LinkedSeries[]) => 
      searchService.augmentSeries(linkedSeries),
  });
};

/**
 * Combined hook that provides both search and augment functionality
 * Useful for search workflows that need both operations
 */
export const useSearch = () => {
  const augmentMutation = useAugmentSeries();

  const searchSeries = (params: SearchParams) => {
    return searchService.searchSeries(params);
  };

  const augmentSeries = (linkedSeries: LinkedSeries[]) => {
    return augmentMutation.mutateAsync(linkedSeries);
  };

  return {
    searchSeries,
    augmentSeries,
    isAugmenting: augmentMutation.isPending,
    augmentError: augmentMutation.error,
  };
};
