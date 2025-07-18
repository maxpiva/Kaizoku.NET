import { apiClient } from '@/lib/api/client';
import { type LinkedSeries, type AugmentedResponse, type SearchSource } from '@/lib/api/types';

export interface SearchParams {
  keyword: string;
  languages?: string;
  searchSources?: string[];
}

export const searchService = {
  /**
   * Gets all available search sources
   * @returns Promise resolving to list of available search sources
   */
  async getAvailableSearchSources(): Promise<SearchSource[]> {
    return apiClient.get<SearchSource[]>('/api/search/sources');
  },

  /**
   * Searches for series across multiple sources
   * @param params Search parameters containing keyword, optional languages, and optional search sources
   * @returns Promise resolving to list of linked series
   */
  async searchSeries(params: SearchParams): Promise<LinkedSeries[]> {
    const searchParams = new URLSearchParams({
      keyword: params.keyword,
      ...(params.languages && { languages: params.languages }),
    });

    // Add search sources as multiple query parameters if provided
    if (params.searchSources && params.searchSources.length > 0) {
      params.searchSources.forEach(sourceId => {
        searchParams.append('searchSources', sourceId);
      });
    }

    return apiClient.get<LinkedSeries[]>(`/api/search?${searchParams.toString()}`);
  },

  /**
   * Augments a list of linked series with full details and type information
   * @param linkedSeries List of linked series to augment
   * @returns Promise resolving to augmented response with series and metadata
   */
  async augmentSeries(linkedSeries: LinkedSeries[]): Promise<AugmentedResponse> {
    return apiClient.post<AugmentedResponse>('/api/search/augment', linkedSeries);
  },
};
