"use client";

import React, { useMemo, memo } from 'react';
import { useQueue, useRemoveFromQueue, useDownloadProgress } from '@/lib/api/hooks/useQueue';
import { useCompletedDownloadsWithCount, useWaitingDownloadsWithCount, useFailedDownloadsWithCount, useManageErrorDownload, useRemoveScheduledDownload } from '@/lib/api/hooks/useDownloads';
import { useSettings } from '@/lib/api/hooks/useSettings';
import { useSearch } from '@/contexts/search-context';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Progress } from '@/components/ui/progress';
import { Trash2, Download, AlertTriangle, CheckCircle, Clock, Smile, Calendar, ExternalLink, RotateCcw, X } from 'lucide-react';
import { ProgressStatus, QueueStatus, type DownloadInfo, type DownloadInfoList, ErrorDownloadAction } from '@/lib/api/types';
import { getApiConfig } from '@/lib/api/config';
import Image from 'next/image';
import type { QueueItem } from '@/lib/api/services/queueService';
import { JobsPanel } from '@/components/kzk/jobs/jobs-panel';

// Extended queue item interface that includes both static queue items and real-time downloads
interface ExtendedQueueItem {
  id: string;
  seriesTitle: string;
  chapterTitle: string;
  thumbnailUrl?: string;
  status: string;
  progress?: number;
  // Optional fields for real-time downloads
  provider?: string;
  scanlator?: string;
  language?: string;
  chapterNumber?: number;
  pageCount?: number;
  message?: string;
  errorMessage?: string;
  isRealTime?: boolean;
  // Original queue item fields
  mangaId?: number;
  chapterIndex?: number;
  retries: number;
  url?: string; // DownloadInfo has url property
}

// Download Card Component - Shared UI for all download panels
const DownloadCard = memo(({ item, onClear, isClearing }: { item: ExtendedQueueItem | DownloadInfo; onClear?: () => void; isClearing?: boolean }) => {
  // Helper function to normalize UTC date strings
  const normalizeUtcString = (dateString: string) => {
    return dateString.includes('Z') || dateString.includes('+') || dateString.includes('-', 10) 
      ? dateString 
      : dateString + 'Z';
  };

  // Check if this is a scheduled download in the future (only for DownloadInfo items)
  const isScheduledForFuture = (() => {
    if ('seriesTitle' in item) return false; // ExtendedQueueItem doesn't have scheduledDateUTC
    
    const scheduledDate = new Date(normalizeUtcString(item.scheduledDateUTC));
    const currentTime = new Date();
    return !item.downloadDateUTC && scheduledDate > currentTime;
  })();

  // Get scheduled date for display (only for DownloadInfo items)
  const scheduledDate = (() => {
    if ('seriesTitle' in item || !isScheduledForFuture) return null;
    return new Date(normalizeUtcString(item.scheduledDateUTC));
  })();

  // Get download date for completed items (only for DownloadInfo items)
  const downloadDate = (() => {
    if ('seriesTitle' in item || !item.downloadDateUTC) return null;
    return new Date(normalizeUtcString(item.downloadDateUTC));
  })();
  // Helper function to format thumbnail URL
  const formatThumbnailUrl = (thumbnailUrl?: string): string => {
    if (!thumbnailUrl) {
      return '/kaizoku.net.png';
    }
    
    // If it already starts with http, return as is
    if (thumbnailUrl.startsWith('http')) {
      return thumbnailUrl;
    }
    
    // Otherwise, prefix with base URL and API path
    const config = getApiConfig();
    return `${config.baseUrl}/api/${thumbnailUrl}`;
  };

  // Determine display data based on item type
  const displayData = 'seriesTitle' in item ? {
    seriesTitle: item.seriesTitle,
    chapterTitle: item.chapterTitle,
    thumbnailUrl: item.thumbnailUrl,
    status: item.status,
    retries: item.retries,
    progress: item.progress,
    provider: item.provider,
    scanlator: item.scanlator,
    chapterNumber: item.chapterNumber,
    errorMessage: item.errorMessage,
    url: item.url, // DownloadInfo has url property
  } : {
    seriesTitle: item.title,
    chapterTitle: item.chapterTitle || `Chapter ${item.chapter}`,
    thumbnailUrl: item.thumbnailUrl, // DownloadInfo has thumbnailUrl property
    status: item.status === QueueStatus.WAITING ? 'waiting' :
            item.status === QueueStatus.RUNNING ? 'downloading' :
            item.status === QueueStatus.COMPLETED ? 'completed' :
            item.status === QueueStatus.FAILED ? 'error' : 'unknown',
    progress: undefined,
    provider: item.provider,
    scanlator: item.scanlator,
    chapterNumber: item.chapter,
    retries: item.retries,
    url: item.url,
    errorMessage: undefined
  };

  const getStatusIcon = (status: string) => {
    // Special case for future scheduled downloads
    if (isScheduledForFuture) {
      return <Calendar className="h-4 w-4 text-yellow-500" />;
    }
    
    switch (status) {
      case 'downloading':
        return <Download className="h-4 w-4 text-blue-500 animate-pulse" />;
      case 'completed':
        return <CheckCircle className="h-4 w-4 text-green-500" />;
      case 'error':
        return <AlertTriangle className="h-4 w-4 text-red-500" />;
      case 'waiting':
        return <Clock className="h-4 w-4 text-yellow-500" />;
      default:
        return <Clock className="h-4 w-4 text-gray-500" />;
    }
  };

  return (
    <Card className={`transition-all duration-200 flex-shrink-0 ${onClear ? 'relative' : ''}`}>
      {/* Clear button positioned at top right when onClear is provided */}
      {onClear && (
        <div className="absolute top-2 right-2 z-10 flex gap-1">
          <Button
            size="sm"
            variant="outline"
            onClick={onClear}
            disabled={isClearing}
            className="h-6 w-6 p-0 hover:bg-red-50 hover:border-red-300"
            title="Remove from queue"
          >
            <X className="h-3 w-3" />
          </Button>
        </div>
      )}
      <CardHeader className="pb-2 p-2">
        <div className="flex items-start gap-3">
          <Image
            src={formatThumbnailUrl(displayData.thumbnailUrl)}
            alt={displayData.seriesTitle}
            width={60}
            height={80}
            className="rounded-md object-cover flex-shrink-0"
            onError={(e) => {
              const target = e.target as HTMLImageElement;
              target.src = '/kaizoku.net.png';
            }}
          />
          <div className="flex-1 min-w-0">
            <CardTitle className="text-base line-clamp-2 leading-tight">
              {displayData.seriesTitle}
            </CardTitle>
            <p className="text-sm text-muted-foreground line-clamp-2 mt-1">
              {displayData.chapterTitle}
            </p>
            <div className='flex items-center gap-2 mt-1'>
              {(displayData.provider || displayData.scanlator) && (
                displayData.url ? (
                  <p
                    className="text-sm text-muted-foreground flex items-center gap-1 cursor-pointer hover:bg-accent/80 transition-colors"
                    onClick={(e) => {
                      e.stopPropagation();
                      if (displayData.url) {
                        window.open(displayData.url, '_blank', 'noopener,noreferrer');
                      }
                    }}
                    title="Click to open the chapter in the source"
                  >
                    <ExternalLink className="h-3 w-3" />
                    {displayData.provider}
                    {(displayData.provider !== displayData.scanlator && displayData.scanlator) ? ` • ${displayData.scanlator}` : ''}
                  </p>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    {displayData.provider}
                    {(displayData.provider !== displayData.scanlator && displayData.scanlator) ? ` • ${displayData.scanlator}` : ''}
                  </p>
                )
              )}
              {getStatusIcon(displayData.status)}
              {isScheduledForFuture && scheduledDate && (
              <div className="text-xs text-muted-foreground font-medium gap-1">
                {scheduledDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
              </div>
              )}
              {displayData.status === 'completed' && downloadDate && (
                <div className="text-xs text-muted-foreground font-medium">
                  {downloadDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </div>
              )}
              {displayData.status === 'downloading' && displayData.progress !== undefined && (
                <span className="text-sm text-muted-foreground font-medium">{displayData.progress}%</span>
              )}
              {displayData.errorMessage && (
                <p className="text-xs text-red-600 mt-2 bg-red-50 p-1 rounded">{displayData.errorMessage}</p>
              )}
              {displayData.retries> 0 && (
                <div className="text-xs text-orange-600 font-medium ml-auto w-auto">
                Retries: {displayData.retries}
                </div>
            )}
            </div>
            {/* Show scheduled time for future scheduled downloads */}
            {displayData.status === 'downloading' && displayData.progress !== undefined && (
              <Progress value={displayData.progress} className="mt-1 w-full h-2" />
            )}
          </div>
        </div>
      </CardHeader>
    </Card>
  );
});

DownloadCard.displayName = 'DownloadCard';

// Error Download Card Component - Special card with Delete and Retry buttons
const ErrorDownloadCard = memo(({ item }: { item: DownloadInfo }) => {
  const manageErrorDownloadMutation = useManageErrorDownload();

  // Helper function to normalize UTC date strings
  const normalizeUtcString = (dateString: string) => {
    return dateString.includes('Z') || dateString.includes('+') || dateString.includes('-', 10) 
      ? dateString 
      : dateString + 'Z';
  };

  // Get download date for completed items
  const downloadDate = item.downloadDateUTC ? new Date(normalizeUtcString(item.downloadDateUTC)) : null;

  // Helper function to format thumbnail URL
  const formatThumbnailUrl = (thumbnailUrl?: string): string => {
    if (!thumbnailUrl) {
      return '/kaizoku.net.png';
    }
    
    if (thumbnailUrl.startsWith('http')) {
      return thumbnailUrl;
    }
    
    const config = getApiConfig();
    return `${config.baseUrl}/api/${thumbnailUrl}`;
  };

  const handleDelete = () => {
    manageErrorDownloadMutation.mutate({ 
      id: item.id, 
      action: ErrorDownloadAction.Delete 
    });
  };

  const handleRetry = () => {
    manageErrorDownloadMutation.mutate({ 
      id: item.id, 
      action: ErrorDownloadAction.Retry 
    });
  };

  return (
    <Card className="transition-all duration-200 flex-shrink-0 relative">
      {/* Action buttons positioned at top right */}
      <div className="absolute top-2 right-2 z-10 flex gap-1">
        <Button
          size="sm"
          variant="outline"
          onClick={handleRetry}
          disabled={manageErrorDownloadMutation.isPending}
          className="h-6 w-6 p-0"
          title="Retry download"
        >
          <RotateCcw className="h-3 w-3" />
        </Button>
        <Button
          size="sm"
          variant="outline"
          onClick={handleDelete}
          disabled={manageErrorDownloadMutation.isPending}
          className="h-6 w-6 p-0 hover:bg-red-50 hover:border-red-300"
          title="Delete download"
        >
          <Trash2 className="h-3 w-3" />
        </Button>
      </div>

      <CardHeader className="pb-2 p-2"> {/* Add right padding for buttons */}
        <div className="flex items-start gap-3">
          <Image
            src={formatThumbnailUrl(item.thumbnailUrl)}
            alt={item.title}
            width={60}
            height={80}
            className="rounded-md object-cover flex-shrink-0"
            onError={(e) => {
              const target = e.target as HTMLImageElement;
              target.src = '/kaizoku.net.png';
            }}
          />
          <div className="flex-1 min-w-0">
            <CardTitle className="text-base line-clamp-2 leading-tight">
              {item.title}
            </CardTitle>
            <p className="text-sm text-muted-foreground line-clamp-2 mt-1">
              {item.chapterTitle || `Chapter ${item.chapter}`}
            </p>
            <div className='flex items-center gap-2 mt-1'>
              {(item.provider || item.scanlator) && (
                item.url ? (
                  <p
                    className="text-sm text-muted-foreground flex items-center gap-1 cursor-pointer hover:bg-accent/80 transition-colors"
                    onClick={(e) => {
                      e.stopPropagation();
                      if (item.url) {
                        window.open(item.url, '_blank', 'noopener,noreferrer');
                      }
                    }}
                    title="Click to open the chapter in the source"
                  >
                    <ExternalLink className="h-3 w-3" />
                    {item.provider}
                    {(item.provider !== item.scanlator && item.scanlator) ? ` • ${item.scanlator}` : ''}
                  </p>
                ) : (
                  <p className="text-sm text-muted-foreground">
                    {item.provider}
                    {(item.provider !== item.scanlator && item.scanlator) ? ` • ${item.scanlator}` : ''}
                  </p>
                )
              )}
              <AlertTriangle className="h-4 w-4 text-red-500" />
              {downloadDate && (
                <div className="text-xs text-muted-foreground font-medium">
                  {downloadDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </div>
              )}
              {item.retries > 0 && (
                <div className="text-xs text-orange-600 font-medium ml-auto w-auto">
                  Retries: {item.retries}
                </div>
              )}
            </div>
          </div>
        </div>
      </CardHeader>
    </Card>
  );
});

ErrorDownloadCard.displayName = 'ErrorDownloadCard';

// Active Downloads Panel - Existing functionality moved here
const ActiveDownloadsPanel = memo(() => {
  const { data: queueItems, isLoading } = useQueue();
  const { downloads, downloadCount } = useDownloadProgress();
  const { debouncedSearchTerm } = useSearch();

  // Helper function to convert ProgressStatus to string
  const getStatusFromProgressStatus = (status: ProgressStatus): string => {
    switch (status) {
      case ProgressStatus.Started:
      case ProgressStatus.InProgress:
        return 'downloading';
      case ProgressStatus.Completed:
        return 'completed';
      case ProgressStatus.Failed:
        return 'error';
      default:
        return 'queued';
    }
  };

  // Combine static queue items with real-time downloads
  const allItems = useMemo(() => {
    const staticItems: ExtendedQueueItem[] = (queueItems || []).map(item => ({
      ...item,
      retries: 0, // Static queue items don't have retries
      isRealTime: false
    }));
    
    const realTimeDownloads: ExtendedQueueItem[] = downloads.map(download => ({
      id: download.id,
      seriesTitle: download.cardInfo.title,
      chapterTitle: download.cardInfo.chapterName,
      thumbnailUrl: download.cardInfo.thumbnailUrl,
      status: getStatusFromProgressStatus(download.status),
      progress: Math.round(download.percentage),
      provider: download.cardInfo.provider,
      scanlator: download.cardInfo.scanlator,
      language: download.cardInfo.language,
      chapterNumber: download.cardInfo.chapterNumber,
      pageCount: download.cardInfo.pageCount,
      message: download.message,
      errorMessage: download.errorMessage,
      retries: 0, // Real-time downloads don't have retries exposed
      isRealTime: true
    }));

    return [...realTimeDownloads, ...staticItems];
  }, [queueItems, downloads]);

  return (
    <Card className="h-full flex flex-col">
      <CardHeader className="p-3 pb-0 flex-shrink-0">
        <CardTitle className="text-md items-center flex">
          Active Downloads
          {downloadCount > 0 && (
            <Badge variant="secondary" className="ml-2 text-xs">{downloadCount}</Badge>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex-1 overflow-auto">
        {isLoading ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-muted-foreground">Loading...</div>
          </div>
        ) : allItems.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center text-muted-foreground">
              <Download className="h-12 w-12 mx-auto mb-4 opacity-50" />
              <p>No active downloads</p>
            </div>
          </div>
        ) : (
          <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
            {allItems.map((item) => (
              <DownloadCard key={item.id} item={item} />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
});

ActiveDownloadsPanel.displayName = 'ActiveDownloadsPanel';

// Completed Downloads Panel
const CompletedDownloadsPanel = memo(() => {
  const { data: settings } = useSettings();
  const { debouncedSearchTerm } = useSearch();
  const limit = settings?.numberOfSimultaneousDownloads || 10;
  
  const { data: completedDownloadsData, isLoading } = useCompletedDownloadsWithCount(
    limit, 
    debouncedSearchTerm.trim() || undefined, // Pass search term to server
    {
      refetchInterval: 5000, // Poll every 5 seconds
      refetchIntervalInBackground: true,
      staleTime: 2000,
    }
  );

  const memoizedDownloads = useMemo(() => completedDownloadsData?.downloads || [], [completedDownloadsData?.downloads]);
  const totalCount = completedDownloadsData?.totalCount || 0;

  return (
    <Card className="h-full flex flex-col">
      <CardHeader className="p-3 pb-0 flex-shrink-0">
        <CardTitle className="text-md items-center flex">
          Latest Downloads
          <Badge variant="secondary"  className="ml-2 text-xs">
            {memoizedDownloads.length}
          </Badge>
          {totalCount > memoizedDownloads.length && (
            <>&nbsp;&nbsp;
              <span className="text-sm text-muted-foreground">of</span><Badge variant="secondary" className="ml-2 text-xs">{totalCount}</Badge>
            </>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex-1 overflow-auto p-2">
        {isLoading ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-muted-foreground">Loading...</div>
          </div>
        ) : memoizedDownloads.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center text-muted-foreground">
              <CheckCircle className="h-12 w-12 mx-auto mb-4 opacity-50" />
              <p>No completed downloads</p>
            </div>
          </div>
        ) : (
          <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
            {memoizedDownloads.map((download) => (
              <DownloadCard key={`${download.title}-${download.chapter}-${download.provider}-${download.scheduledDateUTC}`} item={download} />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
});

CompletedDownloadsPanel.displayName = 'CompletedDownloadsPanel';

// Scheduled Downloads Panel
const ScheduledDownloadsPanel = memo(() => {
  const { data: settings } = useSettings();
  const { debouncedSearchTerm } = useSearch();
  const limit = settings?.numberOfSimultaneousDownloads || 10;
  const removeScheduledDownloadMutation = useRemoveScheduledDownload();

  const { data: scheduledDownloadsData, isLoading } = useWaitingDownloadsWithCount(
    limit,
    debouncedSearchTerm.trim() || undefined, // Pass search term to server
    {
      refetchInterval: 5000, // Poll every 5 seconds
      refetchIntervalInBackground: true,
      staleTime: 2000,
    }
  );

  const memoizedDownloads = useMemo(() => scheduledDownloadsData?.downloads || [], [scheduledDownloadsData?.downloads]);
  const totalCount = scheduledDownloadsData?.totalCount || 0;

  const handleClearDownload = (id: string) => {
    removeScheduledDownloadMutation.mutate(id);
  };

  return (
    <Card className="h-full flex flex-col">
      <CardHeader className="p-3 pb-0 flex-shrink-0">
        <CardTitle className="text-md items-center flex">
          Scheduled Downloads
          <Badge variant="secondary"  className="ml-2 text-xs">
            {memoizedDownloads.length}
          </Badge>
          {totalCount > memoizedDownloads.length && (
            <>&nbsp;&nbsp;
              <span className="text-sm text-muted-foreground">of</span><Badge variant="secondary" className="ml-2 text-xs">{totalCount}</Badge>
            </>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex-1 overflow-auto p-2">
        {isLoading ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-muted-foreground">Loading...</div>
          </div>
        ) : memoizedDownloads.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center text-muted-foreground">
              <Clock className="h-12 w-12 mx-auto mb-4 opacity-50" />
              <p>No scheduled downloads</p>
            </div>
          </div>
        ) : (
          <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
            {memoizedDownloads.map((download) => (
              <DownloadCard
                key={`${download.title}-${download.chapter}-${download.provider}-${download.scheduledDateUTC}`}
                item={download}
                onClear={() => handleClearDownload(download.id)}
                isClearing={removeScheduledDownloadMutation.isPending}
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
});

ScheduledDownloadsPanel.displayName = 'ScheduledDownloadsPanel';

// Error Downloads Panel
const ErrorDownloadsPanel = memo(() => {
  const { data: settings } = useSettings();
  const { debouncedSearchTerm } = useSearch();
  const limit = settings?.numberOfSimultaneousDownloads || 10;
  
  const { data: errorDownloadsData, isLoading } = useFailedDownloadsWithCount(
    limit,
    debouncedSearchTerm.trim() || undefined, // Pass search term to server
    {
      refetchInterval: 30000, // Poll every 30 seconds
      refetchIntervalInBackground: true,
      staleTime: 15000,
    }
  );

  const memoizedDownloads = useMemo(() => errorDownloadsData?.downloads || [], [errorDownloadsData?.downloads]);
  const totalCount = errorDownloadsData?.totalCount || 0;

  return (
    <Card className="h-full flex flex-col">
      <CardHeader className="p-3 pb-0 flex-shrink-0">
        <CardTitle className="text-md items-center flex">
          Error Downloads
          <Badge variant="secondary"  className="ml-2 text-xs">
            {memoizedDownloads.length}
          </Badge>
          {totalCount > memoizedDownloads.length && (
            <>&nbsp;&nbsp;
              <span className="text-sm text-muted-foreground">of</span><Badge variant="secondary" className="ml-2 text-xs">{totalCount}</Badge>
            </>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex-1 overflow-auto p-2">
        {isLoading ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-muted-foreground">Loading...</div>
          </div>
        ) : memoizedDownloads.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <div className="text-center text-muted-foreground">
              <Smile className="h-12 w-12 mx-auto mb-4 opacity-50" />
              <p>No failed downloads</p>
            </div>
          </div>
        ) : (
          <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
            {memoizedDownloads.map((download) => (
              <ErrorDownloadCard key={`${download.title}-${download.chapter}-${download.provider}-${download.scheduledDateUTC}`} item={download} />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  );
});

ErrorDownloadsPanel.displayName = 'ErrorDownloadsPanel';

export default function Queue() {
  const { downloads, downloadCount } = useDownloadProgress();
  const { debouncedSearchTerm } = useSearch();

  return (
    <div className="flex flex-col p-2">
      {/* Five horizontal panels, each taking exactly 16% of the available height with 20% space remaining */}
      <div className="flex-1 flex flex-col gap-3 min-h-0">
        {/* Panel 1: Active Downloads - 16% height */}
        <div className="h-71.5 min-h-0">
          <ActiveDownloadsPanel />
        </div>

        {/* Panel 2: Completed Downloads - 16% height */}
        <div className="h-71.5 min-h-0">
          <CompletedDownloadsPanel />
        </div>

        {/* Panel 3: Scheduled Downloads - 16% height */}
        <div className="h-71.5 min-h-0">
          <ScheduledDownloadsPanel />
        </div>

        {/* Panel 4: Error Downloads - 16% height */}
        <div className="h-71.5 min-h-0">
          <ErrorDownloadsPanel />
        </div>

        {/* Panel 5: Jobs - 16% height */}
        <div className="min-h-0">
          <JobsPanel />
        </div>
      </div>
    </div>
  );
}