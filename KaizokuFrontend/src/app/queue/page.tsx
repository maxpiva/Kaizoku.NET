"use client";

import React, { useMemo, memo, useCallback, useState } from 'react';
import { useDownloadProgress } from '@/lib/api/hooks/useQueue';
import {
  useCompletedDownloadsWithCount,
  useWaitingDownloadsWithCount,
  useFailedDownloadsWithCount,
  useManageErrorDownload,
  useRemoveDownload,
  useClearDownloadsByStatus,
  useRetryAllFailedDownloads,
  useDownloadsMetrics,
} from '@/lib/api/hooks/useDownloads';
import { useSettings } from '@/lib/api/hooks/useSettings';
import { useSearch } from '@/contexts/search-context';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Progress } from '@/components/ui/progress';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '@/components/ui/tabs';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogFooter,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import {
  Trash2,
  Download,
  AlertTriangle,
  CheckCircle,
  Clock,
  Smile,
  Calendar,
  ExternalLink,
  RotateCcw,
  X,
  Activity,
  Layers,
} from 'lucide-react';
import {
  ProgressStatus,
  QueueStatus,
  type DownloadInfo,
  ErrorDownloadAction,
} from '@/lib/api/types';
import Image from 'next/image';
import { JobsPanel } from '@/components/kzk/jobs/jobs-panel';
import { formatThumbnailUrl } from '@/lib/utils/thumbnail';

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

interface ActiveDownloadItem {
  id: string;
  seriesTitle: string;
  chapterTitle: string;
  thumbnailUrl?: string;
  status: 'downloading' | 'completed' | 'error' | 'queued';
  progress: number;
  provider?: string;
  scanlator?: string;
  language?: string;
  chapterNumber?: number;
  pageCount?: number;
  message?: string;
  errorMessage?: string;
  url?: string;
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function normalizeUtcString(dateString: string): string {
  return dateString.includes('Z') ||
    dateString.includes('+') ||
    dateString.includes('-', 10)
    ? dateString
    : dateString + 'Z';
}

function getStatusIcon(status: string, isScheduledForFuture: boolean) {
  if (isScheduledForFuture) {
    return <Calendar className="h-4 w-4 text-amber-500 flex-shrink-0" />;
  }
  switch (status) {
    case 'downloading':
      return <Download className="h-4 w-4 text-blue-500 animate-pulse flex-shrink-0" />;
    case 'completed':
      return <CheckCircle className="h-4 w-4 text-green-500 flex-shrink-0" />;
    case 'error':
      return <AlertTriangle className="h-4 w-4 text-red-500 flex-shrink-0" />;
    case 'waiting':
      return <Clock className="h-4 w-4 text-amber-500 flex-shrink-0" />;
    default:
      return <Clock className="h-4 w-4 text-muted-foreground flex-shrink-0" />;
  }
}

// ---------------------------------------------------------------------------
// Confirmation Dialog
// ---------------------------------------------------------------------------

interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  confirmVariant?: 'destructive' | 'default';
  onConfirm: () => void;
  isPending?: boolean;
}

const ConfirmDialog = memo(
  ({ open, onOpenChange, title, description, confirmLabel, confirmVariant = 'destructive', onConfirm, isPending = false }: ConfirmDialogProps) => (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <DialogFooter className="gap-2 mt-2">
          <Button variant="outline" onClick={() => onOpenChange(false)} disabled={isPending}>Cancel</Button>
          <Button variant={confirmVariant} onClick={onConfirm} disabled={isPending}>
            {isPending ? 'Working…' : confirmLabel}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  ),
);
ConfirmDialog.displayName = 'ConfirmDialog';

// ---------------------------------------------------------------------------
// Metrics Summary Bar
// ---------------------------------------------------------------------------

const MetricsSummary = memo(() => {
  const { data: metrics } = useDownloadsMetrics();
  const active = metrics?.downloads ?? 0;
  const queued = metrics?.queued ?? 0;
  const failed = metrics?.failed ?? 0;

  return (
    <div className="flex flex-wrap justify-center gap-2 mb-3">
      <div className="flex items-center gap-1.5 rounded-md border bg-card px-3 py-1.5 text-sm shadow-sm">
        <Activity className="h-4 w-4 text-blue-500" />
        <span className="font-medium text-blue-500">{active}</span>
        <span className="text-muted-foreground hidden sm:inline">active</span>
      </div>
      <div className="flex items-center gap-1.5 rounded-md border bg-card px-3 py-1.5 text-sm shadow-sm">
        <Clock className="h-4 w-4 text-amber-500" />
        <span className="font-medium text-amber-500">{queued}</span>
        <span className="text-muted-foreground hidden sm:inline">queued</span>
      </div>
      <div className="flex items-center gap-1.5 rounded-md border bg-card px-3 py-1.5 text-sm shadow-sm">
        <AlertTriangle className="h-4 w-4 text-red-500" />
        <span className="font-medium text-red-500">{failed}</span>
        <span className="text-muted-foreground hidden sm:inline">failed</span>
      </div>
    </div>
  );
});
MetricsSummary.displayName = 'MetricsSummary';

// ---------------------------------------------------------------------------
// Tab Header Bar
// ---------------------------------------------------------------------------

const TabHeader = memo(({ title, count, totalCount, children }: { title: string; count: number; totalCount?: number; children?: React.ReactNode }) => (
  <div className="flex items-center justify-between gap-2 mb-3 flex-wrap">
    <div className="flex items-center gap-2">
      <h2 className="text-sm font-semibold">{title}</h2>
      <Badge variant="secondary" className="text-xs">{count}</Badge>
      {totalCount !== undefined && totalCount > count && (
        <>
          <span className="text-xs text-muted-foreground">of</span>
          <Badge variant="outline" className="text-xs">{totalCount}</Badge>
        </>
      )}
    </div>
    {children && <div className="flex items-center gap-2 flex-wrap">{children}</div>}
  </div>
));
TabHeader.displayName = 'TabHeader';

// ---------------------------------------------------------------------------
// Empty / Loading States
// ---------------------------------------------------------------------------

const EmptyState = memo(({ icon, message }: { icon: React.ReactNode; message: string }) => (
  <div className="flex items-center justify-center py-16">
    <div className="text-center text-muted-foreground">
      <div className="flex justify-center mb-4 opacity-40">{icon}</div>
      <p className="text-sm">{message}</p>
    </div>
  </div>
));
EmptyState.displayName = 'EmptyState';

const LoadingState = memo(() => (
  <div className="flex items-center justify-center py-16">
    <div className="text-muted-foreground text-sm">Loading…</div>
  </div>
));
LoadingState.displayName = 'LoadingState';

// ---------------------------------------------------------------------------
// Active Download Card (real-time, no controls)
// ---------------------------------------------------------------------------

const ActiveDownloadCard = memo(({ item }: { item: ActiveDownloadItem }) => {
  const isDownloading = item.status === 'downloading';
  return (
    <Card className="transition-all duration-200 flex-shrink-0">
      <div className="p-2">
        <div className="flex items-start gap-3">
          <Image src={formatThumbnailUrl(item.thumbnailUrl)} alt={item.seriesTitle} width={60} height={80}
            className="rounded-md object-cover flex-shrink-0"
            onError={(e) => { (e.target as HTMLImageElement).src = '/kaizoku.net.png'; }} />
          <div className="flex-1 min-w-0">
            <p className="text-sm font-semibold line-clamp-2 leading-tight">{item.seriesTitle}</p>
            <p className="text-xs text-muted-foreground line-clamp-2 mt-0.5">{item.chapterTitle}</p>
            <div className="flex items-center gap-2 mt-1 flex-wrap">
              {(item.provider || item.scanlator) && (
                item.url ? (
                  <button type="button" className="text-xs text-muted-foreground flex items-center gap-1 hover:text-foreground transition-colors"
                    onClick={() => item.url && window.open(item.url, '_blank', 'noopener,noreferrer')} title="Open chapter source">
                    <ExternalLink className="h-3 w-3" />
                    {item.provider}{item.provider !== item.scanlator && item.scanlator ? ` · ${item.scanlator}` : ''}
                  </button>
                ) : (
                  <p className="text-xs text-muted-foreground">
                    {item.provider}{item.provider !== item.scanlator && item.scanlator ? ` · ${item.scanlator}` : ''}
                  </p>
                )
              )}
              <Download className="h-3.5 w-3.5 text-blue-500 animate-pulse flex-shrink-0" />
              {isDownloading && item.progress > 0 && <span className="text-xs text-muted-foreground font-medium">{item.progress}%</span>}
            </div>
            {isDownloading && item.progress > 0 && <Progress value={item.progress} className="mt-1.5 h-1.5" />}
            {item.errorMessage && (
              <p className="text-xs text-red-600 mt-1.5 bg-red-50 dark:bg-red-950/30 px-1.5 py-1 rounded">{item.errorMessage}</p>
            )}
          </div>
        </div>
      </div>
    </Card>
  );
});
ActiveDownloadCard.displayName = 'ActiveDownloadCard';

// ---------------------------------------------------------------------------
// Removable Download Card (queued / completed — has X button)
// ---------------------------------------------------------------------------

const RemovableDownloadCard = memo(({ item }: { item: DownloadInfo }) => {
  const removeDownload = useRemoveDownload();

  const isScheduledForFuture = useMemo(() => {
    const scheduledDate = new Date(normalizeUtcString(item.scheduledDateUTC));
    return !item.downloadDateUTC && scheduledDate > new Date();
  }, [item.scheduledDateUTC, item.downloadDateUTC]);

  const scheduledDate = useMemo(() => (isScheduledForFuture ? new Date(normalizeUtcString(item.scheduledDateUTC)) : null), [isScheduledForFuture, item.scheduledDateUTC]);
  const downloadDate = useMemo(() => (item.downloadDateUTC ? new Date(normalizeUtcString(item.downloadDateUTC)) : null), [item.downloadDateUTC]);
  const statusString = item.status === QueueStatus.WAITING ? 'waiting' : item.status === QueueStatus.COMPLETED ? 'completed' : 'unknown';

  const handleRemove = useCallback((e: React.MouseEvent) => { e.stopPropagation(); removeDownload.mutate(item.id); }, [item.id, removeDownload]);
  const handleExternalLink = useCallback((e: React.MouseEvent) => { e.stopPropagation(); if (item.url) window.open(item.url, '_blank', 'noopener,noreferrer'); }, [item.url]);

  return (
    <Card className="transition-all duration-200 flex-shrink-0 relative group">
      <button type="button" onClick={handleRemove} disabled={removeDownload.isPending} title="Remove from queue"
        className="absolute top-1.5 right-1.5 z-10 h-5 w-5 rounded-sm flex items-center justify-center bg-background/80 border border-border opacity-0 group-hover:opacity-100 hover:bg-destructive hover:text-destructive-foreground hover:border-destructive transition-all duration-150 disabled:pointer-events-none disabled:opacity-50">
        <X className="h-3 w-3" />
      </button>
      <div className="p-2 pr-7">
        <div className="flex items-start gap-3">
          <Image src={formatThumbnailUrl(item.thumbnailUrl)} alt={item.title} width={60} height={80}
            className="rounded-md object-cover flex-shrink-0"
            onError={(e) => { (e.target as HTMLImageElement).src = '/kaizoku.net.png'; }} />
          <div className="flex-1 min-w-0">
            <p className="text-sm font-semibold line-clamp-2 leading-tight">{item.title}</p>
            <p className="text-xs text-muted-foreground line-clamp-2 mt-0.5">{item.chapterTitle || `Chapter ${item.chapter}`}</p>
            <div className="flex items-center gap-2 mt-1 flex-wrap">
              {(item.provider || item.scanlator) && (
                item.url ? (
                  <button type="button" className="text-xs text-muted-foreground flex items-center gap-1 hover:text-foreground transition-colors" onClick={handleExternalLink} title="Open chapter source">
                    <ExternalLink className="h-3 w-3" />
                    {item.provider}{item.provider !== item.scanlator && item.scanlator ? ` · ${item.scanlator}` : ''}
                  </button>
                ) : (
                  <p className="text-xs text-muted-foreground">{item.provider}{item.provider !== item.scanlator && item.scanlator ? ` · ${item.scanlator}` : ''}</p>
                )
              )}
              {getStatusIcon(statusString, isScheduledForFuture)}
              {isScheduledForFuture && scheduledDate && <span className="text-xs text-muted-foreground font-medium">{scheduledDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>}
              {statusString === 'completed' && downloadDate && <span className="text-xs text-muted-foreground font-medium">{downloadDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>}
              {item.retries > 0 && <span className="text-xs text-orange-600 font-medium ml-auto">Retries: {item.retries}</span>}
            </div>
          </div>
        </div>
      </div>
    </Card>
  );
});
RemovableDownloadCard.displayName = 'RemovableDownloadCard';

// ---------------------------------------------------------------------------
// Error Download Card (retry + delete buttons)
// ---------------------------------------------------------------------------

const ErrorDownloadCard = memo(({ item }: { item: DownloadInfo }) => {
  const manageErrorDownload = useManageErrorDownload();
  const downloadDate = useMemo(() => (item.downloadDateUTC ? new Date(normalizeUtcString(item.downloadDateUTC)) : null), [item.downloadDateUTC]);
  const handleRetry = useCallback(() => manageErrorDownload.mutate({ id: item.id, action: ErrorDownloadAction.Retry }), [item.id, manageErrorDownload]);
  const handleDelete = useCallback(() => manageErrorDownload.mutate({ id: item.id, action: ErrorDownloadAction.Delete }), [item.id, manageErrorDownload]);
  const handleExternalLink = useCallback((e: React.MouseEvent) => { e.stopPropagation(); if (item.url) window.open(item.url, '_blank', 'noopener,noreferrer'); }, [item.url]);

  return (
    <Card className="transition-all duration-200 flex-shrink-0 relative group">
      <div className="absolute top-1.5 right-1.5 z-10 flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity duration-150">
        <button type="button" onClick={handleRetry} disabled={manageErrorDownload.isPending} title="Retry download"
          className="h-5 w-5 rounded-sm flex items-center justify-center bg-background/80 border border-border hover:bg-blue-50 hover:border-blue-300 hover:text-blue-600 dark:hover:bg-blue-950/40 dark:hover:border-blue-700 transition-colors disabled:pointer-events-none disabled:opacity-50">
          <RotateCcw className="h-3 w-3" />
        </button>
        <button type="button" onClick={handleDelete} disabled={manageErrorDownload.isPending} title="Delete download"
          className="h-5 w-5 rounded-sm flex items-center justify-center bg-background/80 border border-border hover:bg-destructive hover:text-destructive-foreground hover:border-destructive transition-colors disabled:pointer-events-none disabled:opacity-50">
          <Trash2 className="h-3 w-3" />
        </button>
      </div>
      <div className="p-2 pr-14">
        <div className="flex items-start gap-3">
          <Image src={formatThumbnailUrl(item.thumbnailUrl)} alt={item.title} width={60} height={80}
            className="rounded-md object-cover flex-shrink-0"
            onError={(e) => { (e.target as HTMLImageElement).src = '/kaizoku.net.png'; }} />
          <div className="flex-1 min-w-0">
            <p className="text-sm font-semibold line-clamp-2 leading-tight">{item.title}</p>
            <p className="text-xs text-muted-foreground line-clamp-2 mt-0.5">{item.chapterTitle || `Chapter ${item.chapter}`}</p>
            <div className="flex items-center gap-2 mt-1 flex-wrap">
              {(item.provider || item.scanlator) && (
                item.url ? (
                  <button type="button" className="text-xs text-muted-foreground flex items-center gap-1 hover:text-foreground transition-colors" onClick={handleExternalLink} title="Open chapter source">
                    <ExternalLink className="h-3 w-3" />
                    {item.provider}{item.provider !== item.scanlator && item.scanlator ? ` · ${item.scanlator}` : ''}
                  </button>
                ) : (
                  <p className="text-xs text-muted-foreground">{item.provider}{item.provider !== item.scanlator && item.scanlator ? ` · ${item.scanlator}` : ''}</p>
                )
              )}
              <AlertTriangle className="h-3.5 w-3.5 text-red-500 flex-shrink-0" />
              {downloadDate && <span className="text-xs text-muted-foreground font-medium">{downloadDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>}
              {item.retries > 0 && <span className="text-xs text-orange-600 font-medium ml-auto">Retries: {item.retries}</span>}
            </div>
          </div>
        </div>
      </div>
    </Card>
  );
});
ErrorDownloadCard.displayName = 'ErrorDownloadCard';

// ---------------------------------------------------------------------------
// Tab Panels
// ---------------------------------------------------------------------------

const ActiveTabPanel = memo(() => {
  const { downloads, downloadCount } = useDownloadProgress();
  const activeItems = useMemo<ActiveDownloadItem[]>(() => downloads.map((d) => ({
    id: d.id, seriesTitle: d.cardInfo.title, chapterTitle: d.cardInfo.chapterName, thumbnailUrl: d.cardInfo.thumbnailUrl,
    status: d.status === ProgressStatus.Started || d.status === ProgressStatus.InProgress ? 'downloading' : d.status === ProgressStatus.Completed ? 'completed' : 'error',
    progress: Math.round(d.percentage), provider: d.cardInfo.provider, scanlator: d.cardInfo.scanlator,
    language: d.cardInfo.language, chapterNumber: d.cardInfo.chapterNumber, pageCount: d.cardInfo.pageCount,
    message: d.message, errorMessage: d.errorMessage, url: d.cardInfo.url,
  })), [downloads]);

  return (
    <div>
      <TabHeader title="Active Downloads" count={downloadCount} />
      {activeItems.length === 0 ? <EmptyState icon={<Download className="h-12 w-12" />} message="No active downloads right now" /> : (
        <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
          {activeItems.map((item) => <ActiveDownloadCard key={item.id} item={item} />)}
        </div>
      )}
    </div>
  );
});
ActiveTabPanel.displayName = 'ActiveTabPanel';

const QueuedTabPanel = memo(() => {
  const { data: settings } = useSettings();
  const { debouncedSearchTerm } = useSearch();
  const limit = settings?.numberOfSimultaneousDownloads || 10;
  const [confirmOpen, setConfirmOpen] = useState(false);
  const { data, isLoading } = useWaitingDownloadsWithCount(limit, debouncedSearchTerm.trim() || undefined, { refetchInterval: 5000, refetchIntervalInBackground: true, staleTime: 2000 });
  const clearDownloads = useClearDownloadsByStatus();
  const downloads = useMemo(() => data?.downloads ?? [], [data?.downloads]);
  const totalCount = data?.totalCount ?? 0;
  const handleClearAll = useCallback(() => { clearDownloads.mutate(QueueStatus.WAITING, { onSuccess: () => setConfirmOpen(false) }); }, [clearDownloads]);

  return (
    <div>
      <TabHeader title="Queued Downloads" count={downloads.length} totalCount={totalCount}>
        {downloads.length > 0 && (
          <Button size="sm" variant="outline" className="h-7 text-xs gap-1.5 hover:bg-destructive/10 hover:border-destructive/50 hover:text-destructive"
            onClick={() => setConfirmOpen(true)} disabled={clearDownloads.isPending}>
            <Trash2 className="h-3 w-3" /> Clear All Queued
          </Button>
        )}
      </TabHeader>
      {isLoading ? <LoadingState /> : downloads.length === 0 ? <EmptyState icon={<Clock className="h-12 w-12" />} message="No downloads in queue" /> : (
        <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
          {downloads.map((item) => <RemovableDownloadCard key={`${item.title}-${item.chapter}-${item.provider}-${item.scheduledDateUTC}`} item={item} />)}
        </div>
      )}
      <ConfirmDialog open={confirmOpen} onOpenChange={setConfirmOpen} title="Clear All Queued Downloads?"
        description={`This will permanently remove all ${totalCount || downloads.length} queued download${(totalCount || downloads.length) !== 1 ? 's' : ''}. This action cannot be undone.`}
        confirmLabel="Clear All" onConfirm={handleClearAll} isPending={clearDownloads.isPending} />
    </div>
  );
});
QueuedTabPanel.displayName = 'QueuedTabPanel';

const CompletedTabPanel = memo(() => {
  const { data: settings } = useSettings();
  const { debouncedSearchTerm } = useSearch();
  const limit = settings?.numberOfSimultaneousDownloads || 10;
  const [confirmOpen, setConfirmOpen] = useState(false);
  const { data, isLoading } = useCompletedDownloadsWithCount(limit, debouncedSearchTerm.trim() || undefined, { refetchInterval: 5000, refetchIntervalInBackground: true, staleTime: 2000 });
  const clearDownloads = useClearDownloadsByStatus();
  const downloads = useMemo(() => data?.downloads ?? [], [data?.downloads]);
  const totalCount = data?.totalCount ?? 0;
  const handleClearHistory = useCallback(() => { clearDownloads.mutate(QueueStatus.COMPLETED, { onSuccess: () => setConfirmOpen(false) }); }, [clearDownloads]);

  return (
    <div>
      <TabHeader title="Completed Downloads" count={downloads.length} totalCount={totalCount}>
        {downloads.length > 0 && (
          <Button size="sm" variant="outline" className="h-7 text-xs gap-1.5 hover:bg-destructive/10 hover:border-destructive/50 hover:text-destructive"
            onClick={() => setConfirmOpen(true)} disabled={clearDownloads.isPending}>
            <Trash2 className="h-3 w-3" /> Clear History
          </Button>
        )}
      </TabHeader>
      {isLoading ? <LoadingState /> : downloads.length === 0 ? <EmptyState icon={<CheckCircle className="h-12 w-12" />} message="No completed downloads" /> : (
        <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
          {downloads.map((item) => <RemovableDownloadCard key={`${item.title}-${item.chapter}-${item.provider}-${item.scheduledDateUTC}`} item={item} />)}
        </div>
      )}
      <ConfirmDialog open={confirmOpen} onOpenChange={setConfirmOpen} title="Clear Download History?"
        description={`This will permanently remove all ${totalCount || downloads.length} completed download record${(totalCount || downloads.length) !== 1 ? 's' : ''}. This action cannot be undone.`}
        confirmLabel="Clear History" onConfirm={handleClearHistory} isPending={clearDownloads.isPending} />
    </div>
  );
});
CompletedTabPanel.displayName = 'CompletedTabPanel';

const ErrorsTabPanel = memo(() => {
  const { data: settings } = useSettings();
  const { debouncedSearchTerm } = useSearch();
  const limit = settings?.numberOfSimultaneousDownloads || 10;
  const [confirmClearOpen, setConfirmClearOpen] = useState(false);
  const [confirmRetryAllOpen, setConfirmRetryAllOpen] = useState(false);
  const { data, isLoading } = useFailedDownloadsWithCount(limit, debouncedSearchTerm.trim() || undefined, { refetchInterval: 30000, refetchIntervalInBackground: true, staleTime: 15000 });
  const clearDownloads = useClearDownloadsByStatus();
  const retryAllFailed = useRetryAllFailedDownloads();
  const downloads = useMemo(() => data?.downloads ?? [], [data?.downloads]);
  const totalCount = data?.totalCount ?? 0;
  const handleClearAll = useCallback(() => { clearDownloads.mutate(QueueStatus.FAILED, { onSuccess: () => setConfirmClearOpen(false) }); }, [clearDownloads]);
  const handleRetryAll = useCallback(() => { retryAllFailed.mutate(undefined, { onSuccess: () => setConfirmRetryAllOpen(false) }); }, [retryAllFailed]);
  const isBulkPending = clearDownloads.isPending || retryAllFailed.isPending;

  return (
    <div>
      <TabHeader title="Failed Downloads" count={downloads.length} totalCount={totalCount}>
        {downloads.length > 0 && (
          <>
            <Button size="sm" variant="outline" className="h-7 text-xs gap-1.5 hover:bg-blue-50 hover:border-blue-300 hover:text-blue-600 dark:hover:bg-blue-950/40 dark:hover:border-blue-700"
              onClick={() => setConfirmRetryAllOpen(true)} disabled={isBulkPending}>
              <RotateCcw className="h-3 w-3" /> Retry All
            </Button>
            <Button size="sm" variant="outline" className="h-7 text-xs gap-1.5 hover:bg-destructive/10 hover:border-destructive/50 hover:text-destructive"
              onClick={() => setConfirmClearOpen(true)} disabled={isBulkPending}>
              <Trash2 className="h-3 w-3" /> Clear All Errors
            </Button>
          </>
        )}
      </TabHeader>
      {isLoading ? <LoadingState /> : downloads.length === 0 ? <EmptyState icon={<Smile className="h-12 w-12" />} message="No failed downloads — great work!" /> : (
        <div className="grid gap-2 md:grid-cols-3 lg:grid-cols-5">
          {downloads.map((item) => <ErrorDownloadCard key={`${item.title}-${item.chapter}-${item.provider}-${item.scheduledDateUTC}`} item={item} />)}
        </div>
      )}
      <ConfirmDialog open={confirmRetryAllOpen} onOpenChange={setConfirmRetryAllOpen} title="Retry All Failed Downloads?"
        description={`This will re-queue all ${totalCount || downloads.length} failed download${(totalCount || downloads.length) !== 1 ? 's' : ''} for another attempt.`}
        confirmLabel="Retry All" confirmVariant="default" onConfirm={handleRetryAll} isPending={retryAllFailed.isPending} />
      <ConfirmDialog open={confirmClearOpen} onOpenChange={setConfirmClearOpen} title="Clear All Failed Downloads?"
        description={`This will permanently delete all ${totalCount || downloads.length} failed download record${(totalCount || downloads.length) !== 1 ? 's' : ''}. This action cannot be undone.`}
        confirmLabel="Clear All" onConfirm={handleClearAll} isPending={clearDownloads.isPending} />
    </div>
  );
});
ErrorsTabPanel.displayName = 'ErrorsTabPanel';

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function Queue() {
  const { downloadCount } = useDownloadProgress();
  const { data: metrics } = useDownloadsMetrics();
  const queuedCount = metrics?.queued ?? 0;
  const failedCount = metrics?.failed ?? 0;

  return (
    <div className="flex flex-col p-2 gap-3">
      <MetricsSummary />
      <Tabs defaultValue="active" className="w-full">
        <div className="flex justify-center">
        <TabsList className="mb-1 flex-wrap h-auto gap-1 w-full sm:w-auto">
          <TabsTrigger value="active" className="gap-1.5 text-xs sm:text-sm">
            <Activity className="h-3.5 w-3.5" /> <span className="hidden sm:inline">Active</span>
            {downloadCount > 0 && <Badge variant="secondary" className="text-xs px-1 py-0 h-4 min-w-[18px]">{downloadCount}</Badge>}
          </TabsTrigger>
          <TabsTrigger value="queued" className="gap-1.5 text-xs sm:text-sm">
            <Clock className="h-3.5 w-3.5" /> <span className="hidden sm:inline">Queued</span>
            {queuedCount > 0 && <Badge variant="secondary" className="text-xs px-1 py-0 h-4 min-w-[18px]">{queuedCount}</Badge>}
          </TabsTrigger>
          <TabsTrigger value="completed" className="gap-1.5 text-xs sm:text-sm">
            <CheckCircle className="h-3.5 w-3.5" /> <span className="hidden sm:inline">Completed</span>
          </TabsTrigger>
          <TabsTrigger value="errors" className="gap-1.5 text-xs sm:text-sm">
            <AlertTriangle className="h-3.5 w-3.5" /> <span className="hidden sm:inline">Errors</span>
            {failedCount > 0 && <Badge variant="destructive" className="text-xs px-1 py-0 h-4 min-w-[18px]">{failedCount}</Badge>}
          </TabsTrigger>
          <TabsTrigger value="jobs" className="gap-1.5 text-xs sm:text-sm">
            <Layers className="h-3.5 w-3.5" /> <span className="hidden sm:inline">Jobs</span>
          </TabsTrigger>
        </TabsList>
        </div>
        <TabsContent value="active"><ActiveTabPanel /></TabsContent>
        <TabsContent value="queued"><QueuedTabPanel /></TabsContent>
        <TabsContent value="completed"><CompletedTabPanel /></TabsContent>
        <TabsContent value="errors"><ErrorsTabPanel /></TabsContent>
        <TabsContent value="jobs"><JobsPanel /></TabsContent>
      </Tabs>
    </div>
  );
}
