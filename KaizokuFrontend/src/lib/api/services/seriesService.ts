import { apiClient } from '@/lib/api/client';
import { type FullSeries, type SeriesInfo, type SeriesExtendedInfo, type ProviderMatch, type AugmentedResponse, type LatestSeriesInfo, type SearchSource, type SeriesIntegrityResult } from '@/lib/api/types';

export const seriesService = {
  /**
   * Add series with full details to the library
   */
  async addSeries(augmentedResponse: AugmentedResponse): Promise<{ id: string }> {
    return apiClient.post<{ id: string }>('/api/series', augmentedResponse);
  },

  /**
   * Get library series (now returns SeriesInfo[])
   */
  async getLibrary(): Promise<SeriesInfo[]> {
    return apiClient.get<SeriesInfo[]>('/api/series/library');
  },
  /**
   * Get individual series by ID with extended information
   */
  async getSeriesById(id: string): Promise<SeriesExtendedInfo> {
    return apiClient.get<SeriesExtendedInfo>(`/api/series?id=${id}`);
  },

  /**
   * Get provider match information by provider ID
   */
  async getMatch(providerId: string): Promise<ProviderMatch | null> {
    return apiClient.get<ProviderMatch | null>(`/api/series/match/${providerId}`);
  },

  /**
   * Set provider match information
   */
  async setMatch(providerMatch: ProviderMatch): Promise<boolean> {
    return apiClient.post<boolean>('/api/series/match', providerMatch);
  },
  /**
   * Update series information
   */
  async updateSeries(seriesData: SeriesExtendedInfo): Promise<SeriesExtendedInfo> {
    return apiClient.patch<SeriesExtendedInfo>('/api/series', seriesData);
  },

  /**
   * Delete series from the library
   */
  async deleteSeries(id: string, alsoPhysical: boolean = false): Promise<void> {
    const params = new URLSearchParams({
      id: id,
      alsoPhysical: alsoPhysical.toString()
    });
    return apiClient.delete<void>(`/api/series?${params.toString()}`);
  },

  /**
   * Get latest series from cloud providers
   * @param start Starting index for pagination
   * @param count Number of items to return
   * @param sourceId Optional source ID filter
   * @param keyword Optional keyword filter
   */
  async getLatest(start: number, count: number, sourceId?: string, keyword?: string): Promise<LatestSeriesInfo[]> {
    const params = new URLSearchParams({
      start: start.toString(),
      count: count.toString(),
    });
    
    if (sourceId) {
      params.append('sourceId', sourceId);
    }
    
    if (keyword) {
      params.append('keyword', keyword);
    }
    
    return apiClient.get<LatestSeriesInfo[]>(`/api/series/latest?${params.toString()}`);
  },

  /**
   * Get available search sources for series search and filtering
   */
  async getSearchSources(): Promise<SearchSource[]> {
    return apiClient.get<SearchSource[]>('/api/search/sources');
  },

  /**
   * Verify integrity of series files
   */
  async verifyIntegrity(id: string): Promise<SeriesIntegrityResult> {
    const params = new URLSearchParams({
      g: id
    });
    return apiClient.get<SeriesIntegrityResult>(`/api/series/verify?${params.toString()}`);
  },

  /**
   * Cleanup series files with integrity issues
   */
  async cleanupSeries(id: string): Promise<void> {
    const params = new URLSearchParams({
      g: id
    });
    return apiClient.get<void>(`/api/series/cleanup?${params.toString()}`);
  },

  /**
   * Update all series naming, filenames and ComicInfo.xml with current selected title
   */
  async updateAllSeries(): Promise<void> {
    return apiClient.post<void>('/api/series/update-all', {});
  },
};
