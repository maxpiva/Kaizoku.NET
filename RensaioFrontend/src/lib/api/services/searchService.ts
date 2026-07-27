import { apiClient } from '@/lib/api/client';
import { type LinkedSeries, type AugmentedResponse, type SearchSource, type DiscoverySources, type DiscoveryStart } from '@/lib/api/types';

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
   * Gets the number of not-installed extensions/sources eligible for discovery search
   * @returns Promise resolving to discovery source counts
   */
  async getDiscoverySources(languages?: string): Promise<DiscoverySources> {
    const params = new URLSearchParams();
    if (languages) params.append('languages', languages);
    const query = params.toString();
    return apiClient.get<DiscoverySources>(`/api/search/discovery/sources${query ? `?${query}` : ''}`);
  },

  /**
   * Starts (or attaches to) an automatic streaming discovery sweep for the query.
   * When the response has done=true the results are already complete (cache hit or disabled);
   * otherwise keep searchId and listen for "DiscoverySearch" events on the progress hub.
   */
  async startDiscovery(keyword: string, languages?: string[]): Promise<DiscoveryStart> {
    return apiClient.post<DiscoveryStart>('/api/search/discovery/start', {
      keyword,
      ...(languages && languages.length > 0 ? { languages } : {}),
    });
  },

  /**
   * Cancels an in-flight discovery sweep (retyped query or closed dialog).
   */
  async cancelDiscovery(searchId: string): Promise<{ cancelled: boolean }> {
    return apiClient.post<{ cancelled: boolean }>(`/api/search/discovery/cancel/${searchId}`, null);
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
