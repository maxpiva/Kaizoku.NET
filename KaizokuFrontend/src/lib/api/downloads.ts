// Export all downloads-related functionality
export { downloadsService } from './services/downloadsService';
export { 
  useDownloadsForSeries,
  useDownloads,
  useDownloadsByStatus,
  useWaitingDownloads,
  useRunningDownloads,
  useCompletedDownloads,
  useFailedDownloads,
  useDownloadStats
} from './hooks/useDownloads';
export { QueueStatus } from './types';
export type { DownloadInfo } from './types';
