import { apiClient } from '@/lib/api/client';
import type { ImportInfo, LinkedSeries, SetupOperationResponse, ImportTotals } from '../types';

export const setupWizardService = {
  /**
   * Scan local files for series
   */
  async scanLocalFiles(): Promise<SetupOperationResponse> {
    return await apiClient.post<SetupOperationResponse>('/api/setup/scan');
  },
  /**
   * Install additional extensions required for the imported series
   */
  async installAdditionalExtensions(): Promise<SetupOperationResponse> {
    return await apiClient.post<SetupOperationResponse>('/api/setup/install-extensions');
  },

  /**
   * Search for series based on imported content
   */
  async searchSeries(): Promise<SetupOperationResponse> {
    return await apiClient.post<SetupOperationResponse>('/api/setup/search');
  },

  /**
   * Get list of pending imports
   */
  async getImports(): Promise<ImportInfo[]> {
    const result = await apiClient.get<ImportInfo[]>('/api/setup/imports');
    return result;
  },

  /**
   * Import series from the provided list
   */
  async importSeries(disableDownloads: boolean = false): Promise<SetupOperationResponse> {
    return await apiClient.post<SetupOperationResponse>(`/api/setup/import?disableDownloads=${disableDownloads}`);
  },

  /**
   * Get import totals for schedule updates step
   */
  async getImportTotals(): Promise<ImportTotals> {
    return await apiClient.get<ImportTotals>('/api/setup/imports/totals');
  },

  /**
   * Import series with disable downloads option
   */
  async importSeriesWithOptions(disableDownloads: boolean): Promise<SetupOperationResponse> {
    return await apiClient.post<SetupOperationResponse>(`/api/setup/import?disableDownloads=${disableDownloads}`);
  },

  /**
   * Augment series information with linked providers
   */
  async augmentSeries(path: string, linkedSeries: LinkedSeries[]): Promise<ImportInfo> {
    return await apiClient.post<ImportInfo>(`/api/setup/augment?path=${encodeURIComponent(path)}`, linkedSeries);
  },

  /**
   * Update import information
   */
  async updateImport(importInfo: ImportInfo): Promise<void> {
    await apiClient.post('/api/setup/update', importInfo);
  },

  /**
   * Look up series by keyword search (uses SearchController)
   */
  async lookupSeries(keyword: string, searchSources?: string[], languages?: string): Promise<LinkedSeries[]> {
    const params = new URLSearchParams();
    params.append('keyword', keyword);
    
    if (languages) {
      params.append('languages', languages);
    }
    
    if (searchSources && searchSources.length > 0) {
      searchSources.forEach(source => {
        params.append('searchSources', source);
      });
    }
    
    return await apiClient.get<LinkedSeries[]>(`/api/search?${params.toString()}`);
  },
};
