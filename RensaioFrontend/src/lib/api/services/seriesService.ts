import { apiClient } from '@/lib/api/client';
import { type FullSeries, type SeriesInfo, type SeriesExtendedInfo, type ProviderMatch, type AugmentedResponse, type LatestSeriesInfo, type LatestGenre, type SearchSource, type SeriesIntegrityResult, type SeriesRenameResult, type ChapterDetail } from '@/lib/api/types';

export const seriesService = {
  /**
   * Add series with full details to the library
   */
  async addSeries(augmentedResponse: AugmentedResponse): Promise<{ id: string }> {
    return apiClient.post<{ id: string }>('/api/serie', augmentedResponse);
  },

  /**
   * Get library series (now returns SeriesInfo[])
   */
  async getLibrary(): Promise<SeriesInfo[]> {
    return apiClient.get<SeriesInfo[]>('/api/serie/library');
  },
  /**
   * Get individual series by ID with extended information
   */
  async getSeriesById(id: string): Promise<SeriesExtendedInfo> {
    return apiClient.get<SeriesExtendedInfo>(`/api/serie?id=${id}`);
  },

  /**
   * Get provider match information by provider ID
   */
  async getMatch(providerId: string): Promise<ProviderMatch | null> {
    return apiClient.get<ProviderMatch | null>(`/api/serie/match/${providerId}`);
  },

  /**
   * Set provider match information
   */
  async setMatch(providerMatch: ProviderMatch): Promise<boolean> {
    return apiClient.post<boolean>('/api/serie/match', providerMatch);
  },
  /**
   * Update series information
   */
  async updateSeries(seriesData: SeriesExtendedInfo): Promise<SeriesExtendedInfo> {
    return apiClient.patch<SeriesExtendedInfo>('/api/serie', seriesData);
  },

  /**
   * Delete series from the library
   */
  async deleteSeries(id: string, alsoPhysical: boolean = false): Promise<void> {
    const params = new URLSearchParams({
      id: id,
      alsoPhysical: alsoPhysical.toString()
    });
    return apiClient.delete<void>(`/api/serie?${params.toString()}`);
  },

  /**
   * Get latest series from cloud providers
   * @param start Starting index for pagination
   * @param count Number of items to return
   * @param sourceId Optional source ID filter
   * @param keyword Optional keyword filter
   * @param genres Optional tag/genre filter; a row must carry every supplied tag (AND semantics)
   */
  async getLatest(start: number, count: number, sourceId?: string, keyword?: string, genres?: string[]): Promise<LatestSeriesInfo[]> {
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

    if (genres && genres.length > 0) {
      for (const g of genres) {
        const trimmed = g.trim();
        if (trimmed) {
          params.append('genre', trimmed);
        }
      }
    }

    return apiClient.get<LatestSeriesInfo[]>(`/api/serie/latest?${params.toString()}`);
  },

  /**
   * Get the distinct tags/genres available in the cached "Latest" cloud catalogue,
   * each with the number of series carrying it (most-used first). Used by the tag filter.
   */
  async getLatestGenres(): Promise<LatestGenre[]> {
    return apiClient.get<LatestGenre[]>('/api/serie/latest/genres');
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
    return apiClient.get<SeriesIntegrityResult>(`/api/serie/verify?${params.toString()}`);
  },

  /**
   * Cleanup series files with integrity issues
   */
  async cleanupSeries(id: string): Promise<void> {
    const params = new URLSearchParams({
      g: id
    });
    return apiClient.get<void>(`/api/serie/cleanup?${params.toString()}`);
  },

  /**
   * Update all series naming, filenames and ComicInfo.xml with current selected title
   */
  async updateAllSeries(): Promise<void> {
    return apiClient.post<void>('/api/serie/update-all', {});
  },

  /**
   * Rename a single series folder to match its title and rename every downloaded
   * .cbz to the canonical naming scheme. Fixes archives saved under an out-of-date name.
   */
  async renameSeries(id: string): Promise<SeriesRenameResult> {
    const params = new URLSearchParams({ id });
    return apiClient.post<SeriesRenameResult>(`/api/serie/rename?${params.toString()}`, {});
  },

  /**
   * Set the release cadence for a series (user override).
   * Stores as negative to prevent auto-recalculation.
   * Null cadenceDays = clear user override.
   */
  async setCadence(seriesId: string, cadenceDays: number | null): Promise<{ releaseCadenceDays: number | null; isUserSet: boolean }> {
    return apiClient.patch<{ releaseCadenceDays: number | null; isUserSet: boolean }>(
      `/api/serie/${seriesId}/cadence`,
      { cadenceDays } as any
    );
  },

  /**
   * Trigger an immediate metadata + new-chapter refresh for a single series.
   * Returns the number of providers queued for refresh.
   */
  async refreshSeries(id: string): Promise<{ success: boolean; queued: number }> {
    const params = new URLSearchParams({ id });
    return apiClient.post<{ success: boolean; queued: number }>(`/api/serie/refresh?${params.toString()}`, {});
  },

  /**
   * Get the unified, series-level chapter list (merged across every source). Each chapter reports
   * whether it is downloaded (and from which source) or genuinely missing, plus the sources
   * available for (re-)download.
   */
  async getSeriesChapters(seriesId: string): Promise<ChapterDetail[]> {
    const params = new URLSearchParams({ seriesId });
    return apiClient.get<ChapterDetail[]>(`/api/serie/chapters?${params.toString()}`);
  },

  /**
   * Re-download (or download) a single chapter. Omit `providerId` for the priority default source
   * (storage → current holder → any available); pass it to force a specific source.
   */
  async redownloadChapter(
    seriesId: string,
    chapterNumber: number,
    providerId?: string
  ): Promise<{ success: boolean; queued: number; sourceProviderName?: string }> {
    const params = new URLSearchParams({ seriesId, chapter: chapterNumber.toString() });
    if (providerId) {
      params.append('providerId', providerId);
    }
    return apiClient.post<{ success: boolean; queued: number; sourceProviderName?: string }>(
      `/api/serie/chapter/redownload?${params.toString()}`,
      {}
    );
  },
};
