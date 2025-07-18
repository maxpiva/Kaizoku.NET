import { useQuery, useMutation, useQueryClient, type UseQueryOptions } from '@tanstack/react-query';
import { downloadsService } from '@/lib/api/services/downloadsService';
import { type DownloadInfo, type DownloadInfoList, type DownloadsMetrics, QueueStatus, ErrorDownloadAction } from '@/lib/api/types';

/**
 * Hook to get downloads for a specific series
 * @param seriesId - The ID of the series
 * @param options - Additional query options
 */
export const useDownloadsForSeries = (
  seriesId: string, 
  options?: Partial<UseQueryOptions<DownloadInfo[], Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'series', seriesId],
    queryFn: () => downloadsService.getDownloadsForSeries(seriesId),
    enabled: !!seriesId,
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Refetch every minute for real-time updates
    ...options, // Allow overriding default options
  });
};

/**
 * Hook to get downloads with optional status filter and limit
 * @param status - Optional queue status filter
 * @param limit - Maximum number of downloads to return (default: 100)
 * @param keyword - Optional keyword to search for in downloads
 * @param options - Additional query options
 */
export const useDownloads = (
  status?: QueueStatus, 
  limit: number = 100, 
  keyword?: string,
  options?: Partial<UseQueryOptions<DownloadInfo[], Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'all', status, limit, keyword],
    queryFn: () => downloadsService.getDownloads(status, limit, keyword),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Refetch every minute for real-time updates
    ...options,
  });
};

/**
 * Hook to get downloads by specific status
 * @param status - The queue status to filter by
 * @param limit - Maximum number of downloads to return (default: 100)
 * @param options - Additional query options
 */
export const useDownloadsByStatus = (
  status: QueueStatus, 
  limit: number = 100, 
  options?: Partial<UseQueryOptions<DownloadInfo[], Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'status', status, limit],
    queryFn: () => downloadsService.getDownloadsByStatus(status, limit),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Refetch every minute for real-time updates
    ...options,
  });
};

/**
 * Hook to get all waiting downloads
 * @param limit - Maximum number of downloads to return (default: 100)
 */
export const useWaitingDownloads = (limit: number = 100) => {
  return useQuery({
    queryKey: ['downloads', 'waiting', limit],
    queryFn: () => downloadsService.getWaitingDownloads(limit),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Refetch every minute for real-time updates
  });
};

/**
 * Hook to get all running downloads
 * @param limit - Maximum number of downloads to return (default: 100)
 */
export const useRunningDownloads = (limit: number = 100) => {
  return useQuery({
    queryKey: ['downloads', 'running', limit],
    queryFn: () => downloadsService.getRunningDownloads(limit),
    staleTime: 15 * 1000, // 15 seconds for more frequent updates on active downloads
    refetchInterval: 30 * 1000, // Refetch every 30 seconds for running downloads
  });
};

/**
 * Hook to get all completed downloads
 * @param limit - Maximum number of downloads to return (default: 100)
 */
export const useCompletedDownloads = (limit: number = 100) => {
  return useQuery({
    queryKey: ['downloads', 'completed', limit],
    queryFn: () => downloadsService.getCompletedDownloads(limit),
    staleTime: 5 * 60 * 1000, // 5 minutes (completed downloads change less frequently)
    refetchInterval: 2 * 60 * 1000, // Refetch every 2 minutes
  });
};

/**
 * Hook to get all failed downloads
 * @param limit - Maximum number of downloads to return (default: 100)
 */
export const useFailedDownloads = (limit: number = 100) => {
  return useQuery({
    queryKey: ['downloads', 'failed', limit],
    queryFn: () => downloadsService.getFailedDownloads(limit),
    staleTime: 2 * 60 * 1000, // 2 minutes
    refetchInterval: 60 * 1000, // Refetch every minute
  });
};

/**
 * Hook to get download statistics by combining all status queries
 * @param limit - Maximum number of downloads per status to fetch (default: 100)
 */
export const useDownloadStats = (limit: number = 100) => {
  const waitingQuery = useWaitingDownloads(limit);
  const runningQuery = useRunningDownloads(limit);
  const completedQuery = useCompletedDownloads(limit);
  const failedQuery = useFailedDownloads(limit);

  return {
    waiting: waitingQuery.data || [],
    running: runningQuery.data || [],
    completed: completedQuery.data || [],
    failed: failedQuery.data || [],
    stats: {
      waitingCount: waitingQuery.data?.length || 0,
      runningCount: runningQuery.data?.length || 0,
      completedCount: completedQuery.data?.length || 0,
      failedCount: failedQuery.data?.length || 0,
      totalCount: (waitingQuery.data?.length || 0) + 
                  (runningQuery.data?.length || 0) + 
                  (completedQuery.data?.length || 0) + 
                  (failedQuery.data?.length || 0),
    },
    isLoading: waitingQuery.isLoading || runningQuery.isLoading || 
               completedQuery.isLoading || failedQuery.isLoading,
    error: waitingQuery.error || runningQuery.error || 
           completedQuery.error || failedQuery.error,
  };
};

/**
 * Hook to get downloads by specific status with total count information
 * @param status - The queue status to filter by
 * @param limit - Maximum number of downloads to return (default: 100)
 * @param keyword - Optional keyword to search for in downloads
 * @param options - Additional query options
 */
export const useDownloadsByStatusWithCount = (
  status: QueueStatus, 
  limit: number = 100, 
  keyword?: string,
  options?: Partial<UseQueryOptions<DownloadInfoList, Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'status-with-count', status, limit, keyword],
    queryFn: () => downloadsService.getDownloadsByStatusWithCount(status, limit, keyword),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Refetch every minute for real-time updates
    ...options,
  });
};

/**
 * Hook to get all waiting downloads with total count
 * @param limit - Maximum number of downloads to return (default: 100)
 * @param keyword - Optional keyword to search for in downloads
 * @param options - Additional query options
 */
export const useWaitingDownloadsWithCount = (
  limit: number = 100,
  keyword?: string,
  options?: Partial<UseQueryOptions<DownloadInfoList, Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'waiting-with-count', limit, keyword],
    queryFn: () => downloadsService.getDownloadsByStatusWithCount(QueueStatus.WAITING, limit, keyword),
    staleTime: 30 * 1000, // 30 seconds
    refetchInterval: 60 * 1000, // Refetch every minute for real-time updates
    ...options,
  });
};

/**
 * Hook to get all running downloads with total count
 * @param limit - Maximum number of downloads to return (default: 100)
 * @param options - Additional query options
 */
export const useRunningDownloadsWithCount = (
  limit: number = 100,
  options?: Partial<UseQueryOptions<DownloadInfoList, Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'running-with-count', limit],
    queryFn: () => downloadsService.getDownloadsByStatusWithCount(QueueStatus.RUNNING, limit),
    staleTime: 15 * 1000, // 15 seconds for more frequent updates on active downloads
    refetchInterval: 30 * 1000, // Refetch every 30 seconds for running downloads
    ...options,
  });
};

/**
 * Hook to get all completed downloads with total count
 * @param limit - Maximum number of downloads to return (default: 100)
 * @param keyword - Optional keyword to search for in downloads
 * @param options - Additional query options
 */
export const useCompletedDownloadsWithCount = (
  limit: number = 100,
  keyword?: string,
  options?: Partial<UseQueryOptions<DownloadInfoList, Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'completed-with-count', limit, keyword],
    queryFn: () => downloadsService.getDownloadsByStatusWithCount(QueueStatus.COMPLETED, limit, keyword),
    staleTime: 5 * 60 * 1000, // 5 minutes (completed downloads change less frequently)
    refetchInterval: 2 * 60 * 1000, // Refetch every 2 minutes
    ...options,
  });
};

/**
 * Hook to get all failed downloads with total count
 * @param limit - Maximum number of downloads to return (default: 100)
 * @param keyword - Optional keyword to search for in downloads
 * @param options - Additional query options
 */
export const useFailedDownloadsWithCount = (
  limit: number = 100,
  keyword?: string,
  options?: Partial<UseQueryOptions<DownloadInfoList, Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'failed-with-count', limit, keyword],
    queryFn: () => downloadsService.getDownloadsByStatusWithCount(QueueStatus.FAILED, limit, keyword),
    staleTime: 2 * 60 * 1000, // 2 minutes
    refetchInterval: 60 * 1000, // Refetch every minute
    ...options,
  });
};

/**
 * Hook to get downloads metrics (counts for active, queued, and failed downloads)
 * @param options - Additional query options
 */
export const useDownloadsMetrics = (
  options?: Partial<UseQueryOptions<DownloadsMetrics, Error>>
) => {
  return useQuery({
    queryKey: ['downloads', 'metrics'],
    queryFn: () => downloadsService.getDownloadsMetrics(),
    staleTime: 5 * 1000, // 5 seconds
    refetchInterval: 10 * 1000, // Refetch every 10 seconds for real-time updates
    refetchIntervalInBackground: true, // Continue polling when window loses focus
    ...options,
  });
};

/**
 * Hook to manage error downloads (delete or retry)
 * @returns Mutation for managing error downloads
 */
export const useManageErrorDownload = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ id, action }: { id: string; action: ErrorDownloadAction }) =>
      downloadsService.manageErrorDownload(id, action),
    onSuccess: () => {
      // Invalidate and refetch failed downloads to get updated list
      queryClient.invalidateQueries({ queryKey: ['downloads', 'failed'] });
      queryClient.invalidateQueries({ queryKey: ['downloads', 'failed-with-count'] });
      queryClient.invalidateQueries({ queryKey: ['downloads', 'metrics'] });
    },
  });
};
