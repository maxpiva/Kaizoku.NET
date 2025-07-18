import { apiClient } from '@/lib/api/client';
import { type DownloadInfo, type DownloadInfoList, type DownloadsMetrics, QueueStatus, ErrorDownloadAction } from '@/lib/api/types';

export const downloadsService = {
  /**
   * Get downloads for a specific series
   * @param seriesId - The ID of the series
   * @returns Promise resolving to a list of download information
   */
  async getDownloadsForSeries(seriesId: string): Promise<DownloadInfo[]> {
    return apiClient.get<DownloadInfo[]>(`/api/downloads/series?seriesId=${seriesId}`);
  },

  /**
   * Get downloads with optional status filter, limit, and keyword search
   * @param status - Optional queue status filter
   * @param limit - Maximum number of downloads to return (default: 100)
   * @param keyword - Optional keyword to search for in downloads
   * @returns Promise resolving to a list of download information
   */
  async getDownloads(status?: QueueStatus, limit: number = 100, keyword?: string): Promise<DownloadInfo[]> {
    const params = new URLSearchParams();
    
    if (status !== undefined) {
      params.append('status', status.toString());
    }
    params.append('limit', limit.toString());
    
    if (keyword) {
      params.append('keyword', keyword);
    }
    
    const queryString = params.toString();
    const url = queryString ? `/api/downloads?${queryString}` : '/api/downloads';
    
    const result = await apiClient.get<DownloadInfoList>(url);
    return result.downloads;
  },

  /**
   * Get downloads by status (returns full DownloadInfoList with totalCount)
   * @param status - The queue status to filter by
   * @param limit - Maximum number of downloads to return (default: 100)
   * @param keyword - Optional keyword to search for in downloads
   * @returns Promise resolving to DownloadInfoList with totalCount and downloads
   */
  async getDownloadsByStatusWithCount(status: QueueStatus, limit: number = 100, keyword?: string): Promise<DownloadInfoList> {
    const params = new URLSearchParams();
    params.append('status', status.toString());
    params.append('limit', limit.toString());
    
    if (keyword) {
      params.append('keyword', keyword);
    }
    
    const url = `/api/downloads?${params.toString()}`;
    return apiClient.get<DownloadInfoList>(url);
  },

  /**
   * Get downloads by status
   * @param status - The queue status to filter by
   * @param limit - Maximum number of downloads to return (default: 100)
   * @returns Promise resolving to a list of download information
   */
  async getDownloadsByStatus(status: QueueStatus, limit: number = 100): Promise<DownloadInfo[]> {
    return this.getDownloads(status, limit);
  },

  /**
   * Get all waiting downloads
   * @param limit - Maximum number of downloads to return (default: 100)
   * @returns Promise resolving to a list of waiting downloads
   */
  async getWaitingDownloads(limit: number = 100): Promise<DownloadInfo[]> {
    return this.getDownloadsByStatus(QueueStatus.WAITING, limit);
  },

  /**
   * Get all running downloads
   * @param limit - Maximum number of downloads to return (default: 100)
   * @returns Promise resolving to a list of running downloads
   */
  async getRunningDownloads(limit: number = 100): Promise<DownloadInfo[]> {
    return this.getDownloadsByStatus(QueueStatus.RUNNING, limit);
  },

  /**
   * Get all completed downloads
   * @param limit - Maximum number of downloads to return (default: 100)
   * @returns Promise resolving to a list of completed downloads
   */
  async getCompletedDownloads(limit: number = 100): Promise<DownloadInfo[]> {
    return this.getDownloadsByStatus(QueueStatus.COMPLETED, limit);
  },

  /**
   * Get all failed downloads
   * @param limit - Maximum number of downloads to return (default: 100)
   * @returns Promise resolving to a list of failed downloads
   */
  async getFailedDownloads(limit: number = 100): Promise<DownloadInfo[]> {
    return this.getDownloadsByStatus(QueueStatus.FAILED, limit);
  },

  /**
   * Get downloads metrics (counts for active, queued, and failed downloads)
   * @returns Promise resolving to downloads metrics
   */
  async getDownloadsMetrics(): Promise<DownloadsMetrics> {
    return apiClient.get<DownloadsMetrics>('/api/downloads/metrics');
  },

  /**
   * Manage error download (delete or retry)
   * @param id - The ID of the download to manage
   * @param action - The action to perform (delete or retry)
   * @returns Promise resolving when the action is complete
   */
  async manageErrorDownload(id: string, action: ErrorDownloadAction): Promise<void> {
    const params = new URLSearchParams();
    params.append('id', id);
    params.append('action', action.toString());
    
    return apiClient.patch<void>(`/api/downloads?${params.toString()}`);
  },
};
