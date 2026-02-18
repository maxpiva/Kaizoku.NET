"use client";

import { useSearchParams, useRouter } from "next/navigation";
import { useState, useEffect, useMemo, memo, useRef, Suspense } from "react";
import { useSeriesById, useSetProviderMatch, useDeleteSeries, useUpdateSeries, useVerifyIntegrity, useCleanupSeries } from "@/lib/api/hooks/useSeries";
import { useDownloadsForSeries } from "@/lib/api/hooks/useDownloads";
import { seriesService } from "@/lib/api/services/seriesService";
import { useQueryClient } from '@tanstack/react-query';
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { Input } from "@/components/ui/input";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Download, Plus, Power, Search, Trash2, Pause, Play, ExternalLink, ShieldCheck, AlertTriangle, CheckCircle, Clock, Calendar } from "lucide-react";
import Image from 'next/image';
import { getApiConfig } from '@/lib/api/config';
import { SeriesStatus, QueueStatus, ArchiveResult, type ProviderExtendedInfo, type DownloadInfo, type ProviderMatch, type ExistingSource, type SeriesExtendedInfo, type SeriesIntegrityResult, type ArchiveIntegrityResult } from "@/lib/api/types";
import { useSeriesContext } from "@/contexts/series-context";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { getStatusDisplay } from "@/lib/utils/series-status";
import { ProviderMatchDialog } from "@/components/dialogs/provider-match-dialog";
import { AddSeries } from "@/components/kzk/series/add-series";


// Provider Card Component
const ProviderCard = ({ provider,
  useCover,
  useTitle,
  useStorage,
  fromChapter,
  seriesId,
  onUseCoverChange,
  onUseTitleChange,
  onUseStorageChange,
  onDisabledChange,
  onDeleteProvider,
  onFromChapterChange,
  deletedProviderStates
}: {
  provider: ProviderExtendedInfo;
  useCover: boolean;
  useTitle: boolean;
  useStorage: boolean;
  fromChapter: string;
  seriesId: string;
  onUseCoverChange: (providerId: string, enabled: boolean) => void; onUseTitleChange: (providerId: string, enabled: boolean) => void;
  onUseStorageChange: (providerId: string, enabled: boolean) => void;
  onDisabledChange: (providerId: string, disabled: boolean) => void;
  onDeleteProvider: (providerId: string) => void;
  onFromChapterChange: (providerId: string, value: string) => void;
  deletedProviderStates: Record<string, boolean>;
}) => {
  const [isEnabled, setIsEnabled] = useState(!provider.isDisabled && !provider.isUninstalled);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const queryClient = useQueryClient();
  
  // Sync isEnabled state with provider.isDisabled prop changes
  useEffect(() => {
    const newIsEnabled = !provider.isDisabled && !provider.isUninstalled;
    setIsEnabled(newIsEnabled);
  }, [provider.isDisabled, provider.isUninstalled, provider.id, provider.provider]);
  
  // Check if thumbnailUrl contains 'unknown' to disable cover functionality
  const hasUnknownThumbnail = provider.thumbnailUrl?.toLowerCase().includes('unknown') ?? false;
  const [matchDialogOpen, setMatchDialogOpen] = useState(false);
  const [providerMatch, setProviderMatch] = useState<ProviderMatch | null>(null);
  const [isLoadingMatch, setIsLoadingMatch] = useState(false);
  // Local state for fromChapter input to avoid updating on every keystroke
  const [localFromChapter, setLocalFromChapter] = useState(fromChapter);
  
  // Sync localFromChapter state with fromChapter prop changes
  useEffect(() => {
    setLocalFromChapter(fromChapter);
  }, [fromChapter]);

  // Reset useCover to false when thumbnail contains 'unknown'
  useEffect(() => {
    if (hasUnknownThumbnail && useCover) {
      onUseCoverChange(provider.id, false);
    }
  }, [hasUnknownThumbnail, useCover, onUseCoverChange, provider.id]);

  // Only use the mutation hook, not the query hook
  const setMatchMutation = useSetProviderMatch();  const handleEnableDisable = () => {
    // If currently enabled (isEnabled=true), button shows "Disable", so clicking should disable it (isDisabled=true)
    // If currently disabled (isEnabled=false), button shows "Enable", so clicking should enable it (isDisabled=false)
    
    // Current enabled state
    const currentlyEnabled = isEnabled;
    
    // FIXED: When clicking "Enable" (currentlyEnabled is false), we want isDisabled=false
    //        When clicking "Disable" (currentlyEnabled is true), we want isDisabled=true
    const newDisabledState = currentlyEnabled; // This is the correct logic!
    
    
    // Send to parent to update backend - the UI state will be updated via props/useEffect
    onDisabledChange(provider.id, newDisabledState);
  };const handleMatch = async () => {
    if (provider.isUnknown) {
      setMatchDialogOpen(true); // Open dialog immediately
      setIsLoadingMatch(true);
      try {
        // Import the service directly to call it manually
        const { seriesService } = await import("@/lib/api/services/seriesService");
        const matchData = await seriesService.getMatch(provider.id);
        setProviderMatch(matchData);
      } catch (error) {
        console.error("Failed to fetch match data:", error);
        setProviderMatch(null); // Set to null on error
      } finally {
        setIsLoadingMatch(false);
      }
    }
  };  const handleMatchSave = (updatedMatch: ProviderMatch) => {
    setMatchMutation.mutate(updatedMatch, {
      onSuccess: () => {
        setMatchDialogOpen(false);
        // Refetch series data instead of full page reload
        queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', seriesId] 
        });
        // Also invalidate library cache since series data changed
        queryClient.invalidateQueries({ 
          queryKey: ['series', 'library'] 
        });
      },
      onError: (error) => {
        console.error("Failed to save match:", error);
      }
    });
  };
  const handleDelete = () => {
    setShowDeleteConfirm(true);
  };

  const handleDeleteConfirm = () => {
    onDeleteProvider(provider.id);
    setShowDeleteConfirm(false);
  };
  const handleDeleteCancel = () => {
    setShowDeleteConfirm(false);
  };
  
  // Handle fromChapter input blur (submit on defocus)
  const handleFromChapterBlur = () => {
    if (localFromChapter !== fromChapter) {
      onFromChapterChange(provider.id, localFromChapter);
    }
  };
  
  // Handle fromChapter input key press (submit on Enter)
  const handleFromChapterKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') {
      e.currentTarget.blur(); // This will trigger onBlur
    }
  };
  
  return (
    <Card className={`transition-all overflow-hidden bg-secondary ${provider.isDisabled ? "bg-opacity-50" : ""}`}>
      <div className="p-3 space-y-2 min-w-0 overflow-hidden relative">
        {/* Action buttons - mobile/tablet: top row, desktop: top-right absolute */}
        <div className="flex flex-wrap gap-2 justify-end mb-2 lg:absolute lg:top-3 lg:right-3 lg:mb-0 lg:z-10">
          <Button
            variant="destructive"
            size="sm"
            onClick={handleDelete}
          >
            <Trash2 className="h-4 w-4 mr-1" />
            <span className="hidden sm:inline">Delete</span>
          </Button>

          {!provider.isUnknown && !provider.isUninstalled && (
            <Button className="opacity-100"
              variant={isEnabled ? "destructive" : "default"}
              size="sm"
              onClick={handleEnableDisable}
            >
              <Power className="h-4 w-4 mr-1" />
              <span className="hidden sm:inline">{isEnabled ? "Disable" : "Enable"}</span>
            </Button>
          )}

          {provider.isUnknown && (
            <Button
              variant="default"
              size="sm"
              onClick={handleMatch}
              disabled={isLoadingMatch}
            >
              <Search className="h-4 w-4 mr-1" />
              <span className="hidden sm:inline">{isLoadingMatch ? "Loading..." : "Match Source"}</span>
            </Button>
          )}
        </div>

        {/* Continue After Chapter input - mobile/tablet: below buttons, desktop: top-right */}
        {!provider.isUnknown && (
          <div className="flex flex-wrap items-center gap-2 justify-end mb-2 lg:absolute lg:top-12 lg:right-3 lg:mb-0">
            <span className="text-muted-foreground text-sm">Continue After Chapter:</span>
            <Input
              type="number"
              step="0.1"
              value={localFromChapter}
              onChange={(e) => setLocalFromChapter(e.target.value)}
              placeholder="Start"
              className="h-8 w-24 text-sm bg-background text-right tabular-nums font-mono"
              disabled={provider.isDisabled}
              onBlur={handleFromChapterBlur}
              onKeyDown={handleFromChapterKeyDown}
            />
          </div>
        )}

        {/* Header section with thumbnail and info */}
        <div className="flex flex-col md:flex-row items-start gap-3 relative min-w-0 overflow-hidden">
          <div className="flex flex-col md:flex-row items-center md:items-start gap-3 flex-1 min-w-0 overflow-hidden w-full">
            {/* Provider Thumbnail */}
            <div className="flex-shrink-0 w-full md:w-auto flex justify-center md:justify-start">
              <img
                src={provider.thumbnailUrl || "/kaizoku.net.png"}
                alt={provider.title} style={{ aspectRatio: '4/6' }}
                className="h-48 md:h-68 max-w-[160px] md:max-w-none object-cover rounded border"
              />
            </div>

            <div className="flex-1 space-y-2 min-w-0 overflow-hidden w-full text-center md:text-left">              <div className="min-w-0 overflow-hidden">
              <CardTitle className="text-lg truncate">{provider.title}</CardTitle>
              { provider.url ? (
              <div className="flex items-center gap-2 text-sm text-muted-foreground cursor-pointer hover:bg-accent/80 transition-colors min-w-0 overflow-hidden flex-wrap justify-center md:justify-start"
                     onClick={(e) => {
                    e.stopPropagation();
                    if (provider.url) {
                      window.open(provider.url, '_blank', 'noopener,noreferrer');
                    }
                  }}
                  title="Click to open in the source"
                ><ExternalLink className="h-4 w-4 flex-shrink-0" />
                <span className="text-lg truncate min-w-0">{provider.provider}{(provider.provider != provider.scanlator && provider.scanlator) ? ` • ${provider.scanlator}` : ''}</span>
                <ReactCountryFlag
                  countryCode={getCountryCodeForLanguage(provider.lang)}
                  svg
                  style={{ width: '20px', height: '15px', borderRadius: '2px', border: '1px solid #ccc' }}
                  title={provider.lang.toUpperCase()}
                />
                <Badge variant="default" className={`ml-2 flex-shrink-0 ${getStatusDisplay(provider.status).color}`}>
                  {getStatusDisplay(provider.status).text}
                </Badge>
              </div>
              ) : (
                 <div className="flex items-center gap-2 text-sm text-muted-foreground min-w-0 overflow-hidden flex-wrap justify-center md:justify-start">
                <span className="text-lg truncate min-w-0">{provider.provider}{(provider.provider != provider.scanlator && provider.scanlator) ? ` • ${provider.scanlator}` : ''}</span>
                <ReactCountryFlag
                  countryCode={getCountryCodeForLanguage(provider.lang)}
                  svg
                  style={{ width: '20px', height: '15px', borderRadius: '2px', border: '1px solid #ccc' }}
                  title={provider.lang.toUpperCase()}
                />
                <Badge variant="default" className={`ml-2 flex-shrink-0 ${getStatusDisplay(provider.status).color}`}>
                  {getStatusDisplay(provider.status).text}
                </Badge>
                </div>
              )}
            </div>{/* Stats grid */}
              <div className="flex flex-wrap gap-2 mt-1 text-sm min-w-0 overflow-hidden justify-center md:justify-start">
                <div className="min-w-0 overflow-hidden">
                  <Badge variant="primary">
                    {provider.chapterList}
                  </Badge>
                  {provider.lastChapter && (
                    <span className="inline-flex flex-wrap items-center gap-1">
                      <span className="text-muted-foreground">
                        Last: <Badge variant="primary">{provider.lastChapter}</Badge>
                      </span>
                      {provider.lastChangeUTC && (
                        <span className="font-medium">
                          {(() => {
                            const utcString = provider.lastChangeUTC.includes('Z') || provider.lastChangeUTC.includes('+') || provider.lastChangeUTC.includes('-', 10)
                              ? provider.lastChangeUTC
                              : provider.lastChangeUTC + 'Z';
                            const date = new Date(utcString);
                            return `${date.toLocaleDateString()} ${date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
                          })()}
                        </span>
                      )}

                    </span>
                  )}

                </div>
              </div>


              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-sm min-w-0 overflow-hidden">
                {provider.author && (
                  <div className="min-w-0 overflow-hidden">
                    <span className="text-muted-foreground">Author:</span>
                    <span className="ml-2 font-medium truncate">{provider.author}</span>
                  </div>
                )}
                {provider.artist && (
                  <div className="min-w-0 overflow-hidden">
                    <span className="text-muted-foreground">Artist:</span>
                    <span className="ml-2 font-medium truncate">{provider.artist}</span>
                  </div>
                )}
              </div>
              <div className="flex flex-wrap gap-1 mt-1 min-w-0 overflow-hidden justify-center md:justify-start">
                {provider.genre && provider.genre.length > 0 && (
                  provider.genre.map((genre) => (
                    <Badge key={genre} variant="primary" className="text-xs flex-shrink-0">
                      {genre}
                    </Badge>
                  ))
                )}
              </div>
              <div className="min-w-0 overflow-hidden mt-1">
                {provider.description && (
                  <p className="text-sm line-clamp-4 break-words overflow-hidden">{provider.description}</p>
                )}
              </div>
              {/* Switches */}              {!provider.isUnknown && (
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 min-w-0">
                  <div className="flex items-center gap-2 min-w-0">
                    <Switch
                      id={`storage-${provider.id}`}
                      checked={useStorage}
                      onCheckedChange={(checked) => onUseStorageChange(provider.id, checked)}
                      disabled={provider.isDisabled}
                      className="flex-shrink-0"
                    />
                    <Label htmlFor={`storage-${provider.id}`} className="text-sm font-medium truncate">
                      Use as Permanent Source
                    </Label>
                  </div>                <div className="flex items-center gap-2 min-w-0">                  <Switch
                    id={`cover-${provider.id}`}
                    checked={useCover}
                    onCheckedChange={hasUnknownThumbnail ? undefined : (checked) => onUseCoverChange(provider.id, checked)}
                    disabled={provider.isDisabled || hasUnknownThumbnail}
                    className="flex-shrink-0"
                  />
                    <Label htmlFor={`cover-${provider.id}`} className="text-sm font-medium truncate">
                      Use Cover
                    </Label>
                  </div>
                  <div className="flex items-center gap-2 min-w-0">
                    <Switch
                      id={`title-${provider.id}`}
                      checked={useTitle}
                      onCheckedChange={(checked) => onUseTitleChange(provider.id, checked)}
                      disabled={provider.isDisabled}
                      className="flex-shrink-0"
                    />
                    <Label htmlFor={`title-${provider.id}`} className="text-sm font-medium truncate">
                      Use Title
                    </Label>
                  </div>

                </div>
              )}


            </div>
          </div>        </div>
      </div>
      {/* Provider Match Dialog */}
      <ProviderMatchDialog
        open={matchDialogOpen}
        onOpenChange={setMatchDialogOpen}
        providerMatch={providerMatch}
        onSave={handleMatchSave}
        isLoading={setMatchMutation.isPending}
        isLoadingData={isLoadingMatch}
        deletedProviderStates={deletedProviderStates}
      />

      {/* Delete Confirmation Dialog */}
      {showDeleteConfirm && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-background border rounded-lg p-6 max-w-md mx-4">
            <h3 className="text-lg font-semibold mb-2">Confirm Delete</h3>
            <p className="text-muted-foreground mb-4">
              Are you sure you want to delete this source? This action cannot be undone.
            </p>            <div className="flex gap-2 justify-end">
              <Button variant="outline" onClick={handleDeleteCancel}>
                Cancel
              </Button>
              <Button variant="destructive" onClick={handleDeleteConfirm}>
                <Trash2 className="h-4 w-4 mr-1" />
                Delete
              </Button>
            </div>
          </div>
        </div>
      )}
    </Card>
  );
};

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

// Helper function to get status icon
const getStatusIcon = (status: QueueStatus, isScheduledForFuture?: boolean) => {
  // Special case for future scheduled downloads
  if (isScheduledForFuture) {
    return <Calendar className="h-4 w-4 text-yellow-500" />;
  }
  
  switch (status) {
    case QueueStatus.RUNNING:
      return <Download className="h-4 w-4 text-blue-500 animate-pulse" />;
    case QueueStatus.COMPLETED:
      return <CheckCircle className="h-4 w-4 text-green-500" />;
    case QueueStatus.FAILED:
      return <AlertTriangle className="h-4 w-4 text-red-500" />;
    case QueueStatus.WAITING:
      return <Clock className="h-4 w-4 text-yellow-500" />;
    default:
      return <Clock className="h-4 w-4 text-gray-500" />;
  }
};

// Download Item Component
const DownloadItem = ({ download }: { download: DownloadInfo }) => {
  // Helper function to normalize UTC date strings
  const normalizeUtcString = (dateString: string) => {
    return dateString.includes('Z') || dateString.includes('+') || dateString.includes('-', 10) 
      ? dateString 
      : dateString + 'Z';
  };

  // Determine which date to display and its label
  const utcDateString = download.downloadDateUTC || download.scheduledDateUTC;
  const dateLabel = download.downloadDateUTC ? 'Downloaded' : 'Scheduled';
  
  // Create Date object from properly normalized UTC string
  const displayDate = new Date(normalizeUtcString(utcDateString));
  const now = new Date();

  // Status color mapping - matches getStatusDisplay badge styling
  const getStatusColor = (status: QueueStatus) => {
    switch (status) {
      case QueueStatus.WAITING:
        return 'bg-yellow-500 text-white';
      case QueueStatus.RUNNING:
        return 'bg-blue-500 text-white';
      case QueueStatus.COMPLETED:
        return 'bg-green-500 text-white';
      case QueueStatus.FAILED:
        return 'bg-red-500 text-white';
      default:
        return 'bg-gray-500 text-white';
    }
  };

  // Status text mapping - consistent with series status display
  const getStatusText = (status: QueueStatus) => {
    if (status === QueueStatus.WAITING) {
      // If scheduled in the future, show 'Scheduled' instead of 'Waiting'
      if (displayDate > now) return 'Scheduled';
      return 'Waiting';
    }
    switch (status) {
      case QueueStatus.RUNNING:
        return 'Running';
      case QueueStatus.COMPLETED:
        return 'Completed';
      case QueueStatus.FAILED:
        return 'Failed';
      default:
        return 'Unknown';
    }
  };

  // Determine if we should show the date/time
  let showDate = false;
  if (download.status === QueueStatus.WAITING) {
    // Only show if scheduled in the future
    showDate = displayDate > now;
  } else if (download.status === QueueStatus.COMPLETED || download.status === QueueStatus.FAILED) {
    showDate = true;
  } 
  // Do not show for RUNNING status

  return (
    <Card className="transition-all duration-200 flex-shrink-0 overflow-hidden">
      <CardHeader className="pb-2 p-2">
        <div className="flex items-start gap-3 min-w-0 overflow-hidden">
          <Image
            src={download.thumbnailUrl ? formatThumbnailUrl(download.thumbnailUrl) : '/kaizoku.net.png'}
            alt={download.title || 'Download'}
            width={60}
            height={80}
            className="rounded-md object-cover flex-shrink-0"
            onError={(e: React.SyntheticEvent<HTMLImageElement, Event>) => {
              const target = e.target as HTMLImageElement;
              target.src = '/kaizoku.net.png';
            }}
          />
          <div className="flex-1 min-w-0 overflow-hidden">
            <CardTitle className="text-base line-clamp-2 leading-tight break-words">
              {download.title || 'Unknown Series'}
            </CardTitle>
            <p className="text-sm text-muted-foreground line-clamp-2 mt-1 break-words">
              {download.chapterTitle ? download.chapterTitle : `Chapter ${download.chapter}`}
            </p>
            <div className='flex flex-wrap items-center gap-2 mt-1 min-w-0'>
              {(download.provider || download.scanlator) && (
                download.url ? (
                  <p
                    className="text-sm text-muted-foreground flex items-center gap-1 cursor-pointer hover:bg-accent/80 transition-colors truncate min-w-0"
                    onClick={(e) => {
                      e.stopPropagation();
                      if (download.url) {
                        window.open(download.url, '_blank', 'noopener,noreferrer');
                      }
                    }}
                    title="Click to open the chapter in the source"
                  >
                    <ExternalLink className="h-3 w-3 flex-shrink-0" />
                    <span className="truncate">{download.provider}
                    {(download.provider !== download.scanlator && download.scanlator) ? ` • ${download.scanlator}` : ''}</span>
                  </p>
                ) : (
                  <p className="text-sm text-muted-foreground truncate min-w-0">
                    {download.provider}
                    {(download.provider !== download.scanlator && download.scanlator) ? ` • ${download.scanlator}` : ''}
                  </p>
                )
              )}
              {getStatusIcon(download.status, download.status === QueueStatus.WAITING && displayDate > now)}
              {showDate && (
                <div className="text-xs text-muted-foreground font-medium flex-shrink-0">
                  {download.status === QueueStatus.COMPLETED || download.status === QueueStatus.FAILED ? (
                    <>
                      {displayDate.toLocaleDateString()}&nbsp;
                      {displayDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                    </>
                  ) : (
                    displayDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
                  )}
                </div>
              )}
              {download.retries > 0 && (
                <div className="text-xs text-orange-600 font-medium ml-auto flex-shrink-0">
                  Retries: {download.retries}
                </div>
              )}
            </div>
          </div>
        </div>
      </CardHeader>
    </Card>
  );
};

/**
 * Downloads Panel Component - Fully disconnected from ProviderExtendedInfo
 * 
 * Features:
 * - Uses new getDownloadsForSeries API endpoint
 * - Live polling every 10 seconds for real-time updates
 * - Memoized component to prevent unnecessary re-renders
 * - Memoized sorted downloads array for performance
 * - Loading indicator during data fetch
 * - Error handling with fallback UI
 * - Only re-renders when downloads data actually changes
 * - Auto-refreshes series and providers when downloads complete
 * - Properly handles series deletion to prevent infinite loops
 */
const DownloadsPanel = memo(({ seriesId, isDeleting }: { seriesId: string; isDeleting: boolean }) => {
  const queryClient = useQueryClient();
  
  // Track previous downloads state to detect completion
  const previousDownloadsRef = useRef<DownloadInfo[] | null>(null);
  
  // Fetch downloads with live polling every 10 seconds, but disable when deleting
  const { data: downloads, isLoading: downloadsLoading, error: downloadsError } = useDownloadsForSeries(seriesId, {
    refetchInterval: isDeleting ? false : 10000, // Stop polling when deleting
    refetchIntervalInBackground: !isDeleting, // Stop background polling when deleting
    staleTime: 5000, // Consider data stale after 5 seconds
    enabled: !isDeleting, // Disable query entirely when deleting
  });

  // Detect when active downloads (waiting/running) complete and trigger series refresh
  useEffect(() => {
    // Skip all logic if we're in the process of deleting
    if (isDeleting) {
      return;
    }
    
    if (!downloads || !previousDownloadsRef.current) {
      // First load or no previous data - just store current state
      previousDownloadsRef.current = downloads || null;
      return;
    }

    const previousDownloads = previousDownloadsRef.current;
    const currentDownloads = downloads;

    // Check if previous downloads had waiting or running items
    const previousActiveDownloads = previousDownloads.filter(
      download => download.status === QueueStatus.WAITING || download.status === QueueStatus.RUNNING
    );

    // Check if current downloads have waiting or running items  
    const currentActiveDownloads = currentDownloads.filter(
      download => download.status === QueueStatus.WAITING || download.status === QueueStatus.RUNNING
    );

    const hadActiveDownloads = previousActiveDownloads.length > 0;
    const hasActiveDownloads = currentActiveDownloads.length > 0;

    // If we had active downloads before but don't now, trigger series refresh
    if (hadActiveDownloads && !hasActiveDownloads) {
      
      // Small delay to ensure backend has processed the completion and updated series data
      setTimeout(() => {
        // Only refresh if we're not deleting the series
        if (!isDeleting) {
          // Refresh both series data and providers data
          queryClient.invalidateQueries({ 
            queryKey: ['series', 'detail', seriesId] 
          });
          
          // Also refresh sources/providers to get updated chapter counts and metadata
          queryClient.invalidateQueries({ 
            queryKey: ['series', 'sources'] 
          });
          
        }
      }, 1000);
    }

    // Update the previous state
    previousDownloadsRef.current = downloads;
  }, [downloads, seriesId, queryClient, isDeleting]);

  // Memoize sorted downloads to prevent unnecessary re-renders
  const sortedDownloads = useMemo(() => {
    if (!downloads?.length) return [];
    
    return [...downloads].sort((a, b) => {
      //const dateA = a.downloadDateUTC ? new Date(a.downloadDateUTC) : new Date(a.scheduledDateUTC);
      //const dateB = b.downloadDateUTC ? new Date(b.downloadDateUTC) : new Date(b.scheduledDateUTC);
      const dateA = new Date(a.scheduledDateUTC);
      const dateB = new Date(b.scheduledDateUTC);
      return dateB.getTime() - dateA.getTime();
    });
  }, [downloads]);

  if (downloadsError) {
    return (
      <Card className="lg:col-span-1 flex flex-col overflow-hidden min-w-0">
        <CardHeader className="pl-4 pr-4 pt-4 pb-0">
          <CardTitle className="flex items-center gap-2">
            <Download className="h-5 w-5 flex-shrink-0" />
            <span className="truncate">Latest Downloads</span>
          </CardTitle>
        </CardHeader>
        <CardContent className="p-4 flex-1 min-w-0">
          <div className="text-center text-muted-foreground py-3">
            <Download className="h-12 w-12 mx-auto mb-4 opacity-50" />
            <p>Failed to load downloads</p>
          </div>
        </CardContent>
      </Card>
    );
  }

  return (
    <Card className="lg:col-span-1 flex flex-col overflow-hidden min-w-0">
      <CardHeader className="pl-4 pr-4 pt-4 pb-0">
        <CardTitle className="flex items-center gap-2 flex-wrap">
          <Download className="h-5 w-5 flex-shrink-0" />
          <span className="truncate">Latest Downloads</span>
          {sortedDownloads.length > 0 && (
            <Badge variant="secondary" className="ml-2 text-xs flex-shrink-0">
              {sortedDownloads.length}
            </Badge>
          )}
          {downloadsLoading && (
            <div className="animate-spin rounded-full h-4 w-4 border-b-2 border-primary flex-shrink-0"></div>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="flex-1 overflow-auto p-3 min-w-0">
        {sortedDownloads.length > 0 ? (
          <div className="space-y-2">
            {sortedDownloads.map((download, index) => (
              <DownloadItem 
                key={`${download.title}-${download.chapter}-${download.provider}-${download.scheduledDateUTC}-${index}`} 
                download={download} 
              />
            ))}
          </div>
        ) : (
          <div className="flex items-center justify-center h-full">
            <div className="text-center text-muted-foreground">
              <Download className="h-12 w-12 mx-auto mb-4 opacity-50" />
              <p>No downloads yet</p>
            </div>
          </div>
        )}
      </CardContent>
    </Card>
  );
});

DownloadsPanel.displayName = 'DownloadsPanel';

export default function SeriesPage() {
  return (
    <Suspense fallback={<div className="flex items-center justify-center h-64">Loading...</div>}>
      <SeriesPageContent />
    </Suspense>
  );
}

function SeriesPageContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const seriesId = searchParams.get('id');
  const { setSeriesTitle } = useSeriesContext();
  const queryClient = useQueryClient();

  // Track deletion state to prevent loops
  const [isDeleting, setIsDeleting] = useState(false);

  const { data: series, isLoading, error } = useSeriesById(seriesId || '', !isDeleting);
  const deleteSeries = useDeleteSeries();
  const updateSeriesMutation = useUpdateSeries();
  const verifyIntegrity = useVerifyIntegrity();
  const cleanupSeries = useCleanupSeries();
  
  // Provider switch state management
  const [providerSwitches, setProviderSwitches] = useState<Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }>>({});

  // Provider disabled state management  
  const [providerDisabledStates, setProviderDisabledStates] = useState<Record<string, boolean>>({});  // Provider deleted state management  
  const [providerDeletedStates, setProviderDeletedStates] = useState<Record<string, boolean>>({});

  // Pause downloads state management
  const [pausedDownloads, setPausedDownloads] = useState<boolean>(false);  // Provider fromChapter state management
  const [providerFromChapters, setProviderFromChapters] = useState<Record<string, string>>({});

  // Delete dialog state management
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [deletePhysicalFiles, setDeletePhysicalFiles] = useState(false);
  
  // Verify integrity dialog state management
  const [showVerifyDialog, setShowVerifyDialog] = useState(false);
  const [verifyResult, setVerifyResult] = useState<SeriesIntegrityResult | null>(null);
  const [showCleanupDialog, setShowCleanupDialog] = useState(false);
  
  // Track user activity for periodic refresh logic
  const lastActivityRef = useRef<number>(Date.now());
  const lastSeriesDataRef = useRef<SeriesExtendedInfo | null>(null);
  
  // Track user activity (mouse, keyboard, touch events)
  useEffect(() => {
    const updateActivity = () => {
      lastActivityRef.current = Date.now();
    };

    const events = ['mousedown', 'keypress', 'scroll', 'touchstart', 'click'];
    
    events.forEach(event => {
      document.addEventListener(event, updateActivity, true);
    });

    return () => {
      events.forEach(event => {
        document.removeEventListener(event, updateActivity, true);
      });
    };
  }, []);

  // Periodic refresh of series data when user is idle
  useEffect(() => {
    if (!seriesId || isDeleting) return;

    const interval = setInterval(async () => {
      const now = Date.now();
      const timeSinceLastActivity = now - lastActivityRef.current;
      const oneMinuteInMs = 60 * 1000;
      
      // Only refresh if user has been idle for at least 1 minute
      if (timeSinceLastActivity < oneMinuteInMs) return;

      try {
        // Get fresh data from server
        const freshSeriesData = await seriesService.getSeriesById(seriesId);
        
        // Compare with previous data using memo-like logic
        const hasChanges = !lastSeriesDataRef.current || 
          JSON.stringify(lastSeriesDataRef.current) !== JSON.stringify(freshSeriesData);

        if (hasChanges) {
          // Update the query cache with fresh data
          queryClient.setQueryData(['series', 'detail', seriesId], freshSeriesData);
          
          // Store the new data for next comparison
          lastSeriesDataRef.current = freshSeriesData;
          
          console.log('Series data refreshed due to changes detected (user idle)');
        }
      } catch (error) {
        console.error('Failed to refresh series data:', error);
      }
    }, 60000); // Check every 60 seconds

    return () => clearInterval(interval);
  }, [seriesId, isDeleting, queryClient]);

  // Store series data for comparison on each update
  useEffect(() => {
    if (series && !isDeleting) {
      lastSeriesDataRef.current = series;
    }
  }, [series, isDeleting]);
  
  // Helper function that allows updating with an explicit pausedDownloads value
  const updateSeriesWithPausedDownloads = async (overridePausedDownloads: boolean) => {
    if (!series || isDeleting) return;
    
    try {
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: overridePausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          const fromChapterValue = providerFromChapters[provider.id];
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          return updatedProvider;
        })
      };

      // Check if any provider is deleted
      const hasDeletedProviders = updatedSeries.providers.some(p => p.isDeleted);

      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend
      // This ensures that if the backend changed any values (like fromChapter), the UI reflects them
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
        });
        
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation to prevent unnecessary refetching
      // Update the React Query cache directly with the result from the backend
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Library invalidation is now handled by the useUpdateSeries hook automatically
      
      // Only invalidate if provider count actually changed (to handle edge cases)
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }
    } catch (error) {
      console.error('Failed to update series:', error);
    }
  };

  // Standard update series function (uses current pausedDownloads state)
  const updateSeries = async () => {
    // Call the override function with the current pausedDownloads value
    await updateSeriesWithPausedDownloads(pausedDownloads);
  };

  // Generic update function that accepts explicit switch values
  const updateSeriesWithSwitches = async (providerId: string, newSwitches: { useTitle?: boolean; useCover?: boolean; useStorage?: boolean }) => {
    if (!series || isDeleting) return;
    
    try {
      // Create a temporary modified switches object with the new values
      const effectiveSwitches = {
        ...providerSwitches,
        [providerId]: {
          ...providerSwitches[providerId],
          ...newSwitches
        }
      };
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          // Use the modified switches for the specific provider
          const currentSwitches = provider.id === providerId ? 
            effectiveSwitches[providerId] : 
            (providerSwitches[provider.id] || { useTitle: false, useCover: false, useStorage: false });
          const fromChapterValue = providerFromChapters[provider.id];
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: currentSwitches?.useTitle ?? provider.useTitle,
            useCover: currentSwitches?.useCover ?? provider.useCover,
            isStorage: currentSwitches?.useStorage ?? provider.isStorage,
          };
          
          
          return updatedProvider;
        })
      };



      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend if needed
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        const updatedSwitches: Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
          // Update switches with backend response
          updatedSwitches[provider.id] = {
            useTitle: provider.useTitle ?? false,
            useCover: provider.useCover ?? false,
            useStorage: provider.isStorage ?? false
          };
        });
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Update the switches with values from backend response
        setProviderSwitches(prev => ({
          ...prev,
          ...updatedSwitches
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Always invalidate library cache when series data changes (for tab filtering)
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'library'] 
      });
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with switches:', error);
    }
  };

  // Helper function to update with explicit disabled state
  const updateSeriesWithDisabledState = async (providerId: string, disabledState: boolean) => {
    if (!series || isDeleting) return;
    
    try {
      // Create a temporary modified disabled states object with the new value
      const effectiveDisabledStates = {
        ...providerDisabledStates,
        [providerId]: disabledState
      };
      
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          const fromChapterValue = providerFromChapters[provider.id];
          // Use the modified disabled state for the specific provider
          const currentDisabledState = provider.id === providerId ? 
            disabledState : 
            (providerDisabledStates[provider.id] ?? provider.isDisabled);
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : currentDisabledState,
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          
          
          return updatedProvider;
        })
      };


      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend if needed
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
        });
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Always invalidate library cache when series data changes (for tab filtering)
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'library'] 
      });
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with disabled state:', error);
    }
  };

  // Helper function to update with explicit deleted state
  const updateSeriesWithDeletedState = async (providerId: string, deletedState: boolean) => {
    if (!series || isDeleting) return;
    
    try {
      // Create a temporary modified deleted states object with the new value
      const effectiveDeletedStates = {
        ...providerDeletedStates,
        [providerId]: deletedState
      };
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          const fromChapterValue = providerFromChapters[provider.id];
          // Use the modified deleted state for the specific provider
          const currentDeletedState = provider.id === providerId ? 
            deletedState : 
            (providerDeletedStates[provider.id] ?? false);
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: currentDeletedState,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          
          return updatedProvider;
        })
      };


      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend if needed
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        const updatedDeletedStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
          // Update deleted states with backend response
          updatedDeletedStates[provider.id] = provider.isDeleted ?? false;
        });
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Update the deleted states with values from backend response
        setProviderDeletedStates(prev => ({
          ...prev,
          ...updatedDeletedStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Always invalidate library cache when series data changes (for tab filtering)
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'library'] 
      });
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with deleted state:', error);
    }
  };

  // Cleanup effect to handle component unmount during deletion
  useEffect(() => {
    return () => {
      // If component is unmounting and we're deleting, cancel any ongoing queries
      if (isDeleting && seriesId) {
        queryClient.cancelQueries({ queryKey: ['series', 'detail', seriesId] });
        queryClient.cancelQueries({ queryKey: ['downloads', 'series', seriesId] });
      }
    };
  }, [isDeleting, seriesId, queryClient]);

  // Update series title in context when data loads
  useEffect(() => {
    // Don't update title if we're deleting
    if (isDeleting) {
      return;
    }
    
    if (series?.title) {
      setSeriesTitle(series.title);
    } else if (!seriesId) {
      setSeriesTitle('Error');
    } else if (isLoading) {
      setSeriesTitle('Loading...');
    } else if (error || !series) {
      setSeriesTitle('Error');
    }
  }, [series, isLoading, error, seriesId, setSeriesTitle, isDeleting]);  // Initialize provider switches from the series data (only when local state is empty)
  useEffect(() => {
    // Don't initialize if we're deleting
    if (isDeleting) {
      return;
    }
    
    if (series && series.providers) {
      // Only initialize if local state is empty (first load or series change)
      const hasLocalState = Object.keys(providerSwitches).length > 0;
      const seriesIdChanged = series.id !== searchParams.get('id');
      
      if (!hasLocalState || seriesIdChanged) {
        const initialSwitches: Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }> = {};
        const initialDisabledStates: Record<string, boolean> = {};
        const initialFromChapters: Record<string, string> = {};
        
        series.providers.forEach(provider => {
          const hasUnknownThumbnail = provider.thumbnailUrl?.toLowerCase().includes('unknown') ?? false;
          initialSwitches[provider.id] = {
            useTitle: provider.useTitle ?? false,
            useCover: hasUnknownThumbnail ? false : (provider.useCover ?? false),
            useStorage: provider.isStorage ?? false
          };
          // If provider is uninstalled, it should be treated as disabled
          initialDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
          initialFromChapters[provider.id] = provider.fromChapter?.toString() || "";
        });

        setProviderSwitches(initialSwitches);
        setProviderDisabledStates(initialDisabledStates);
        setProviderFromChapters(initialFromChapters);
        setPausedDownloads(series.pausedDownloads ?? false);
      }
    }
  }, [series, providerSwitches, searchParams, isDeleting]);
  // Compute derived values (only when series is available)
  const activeProviderForTitle = series?.providers.find(p =>
    !providerDeletedStates[p.id] && providerSwitches[p.id]?.useTitle
  );
  const activeProviderForCover = series?.providers.find(p =>
    !providerDeletedStates[p.id] && providerSwitches[p.id]?.useCover
  );// Check if all providers are disabled (including UI state changes, excluding unknown, uninstalled, and deleted providers)
  const knownProviders = series?.providers?.filter(provider =>
    !provider.isUnknown &&
    !provider.isUninstalled &&
    !providerDeletedStates[provider.id]
  ) || [];
  const allProvidersDisabled = knownProviders.length > 0 && knownProviders.every(provider => {
    const isDisabled = providerDisabledStates[provider.id] ?? provider.isDisabled;
    return isDisabled;
  });
  // Determine the display title and thumbnail based on active providers
  const displayTitle = activeProviderForTitle?.title || series?.title || '';
  const displayThumbnail = activeProviderForCover?.thumbnailUrl || series?.thumbnailUrl;
  
  // Calculate effective isActive state based on current provider states
  // A series is considered active if it has at least one enabled, known provider
  const hasActiveProviders = knownProviders.some(provider => {
    const isDisabled = providerDisabledStates[provider.id] ?? provider.isDisabled;
    return !isDisabled;
  });
  
  // Determine the effective series status
  const effectiveStatus = series && (!hasActiveProviders || allProvidersDisabled) ? SeriesStatus.DISABLED : (series?.status ?? SeriesStatus.UNKNOWN);
  const statusDisplay = getStatusDisplay(effectiveStatus);

  // Update series title in context when display title changes
  useEffect(() => {
    // Don't update title if we're deleting
    if (isDeleting) {
      return;
    }
    
    if (displayTitle) {
      setSeriesTitle(displayTitle);
    }
  }, [displayTitle, setSeriesTitle, isDeleting]);

  // Handler for use title changes - enforce exclusivity
  const handleUseTitleChange = async (providerId: string, enabled: boolean) => {
    // Build the complete new state for all providers
    const allUpdates: Record<string, { useTitle?: boolean }> = {};
    
    if (enabled) {
      // Turn off useTitle for all other providers
      Object.keys(providerSwitches).forEach(id => {
        if (id !== providerId) {
          allUpdates[id] = { useTitle: false };
        }
      });
    }
    
    // Set the requested provider's useTitle
    allUpdates[providerId] = { useTitle: enabled };

    // Update state for all affected providers
    setProviderSwitches(prev => {
      const newSwitches = { ...prev };

      Object.keys(allUpdates).forEach(id => {
        if (newSwitches[id]) {
          newSwitches[id] = {
            ...newSwitches[id],
            ...allUpdates[id]
          };
        }
      });

      return newSwitches;
    });

    // Send a single update with all changes using the new values directly
    await updateSeriesWithCompleteState(allUpdates);
  };
  // Handler for use cover changes - enforce exclusivity
  const handleUseCoverChange = async (providerId: string, enabled: boolean) => {
    // Build the complete new state for all providers
    const allUpdates: Record<string, { useCover?: boolean }> = {};
    
    if (enabled) {
      // Turn off useCover for all other providers
      Object.keys(providerSwitches).forEach(id => {
        if (id !== providerId) {
          allUpdates[id] = { useCover: false };
        }
      });
    }
    
    // Set the requested provider's useCover
    allUpdates[providerId] = { useCover: enabled };

    // Update state for all affected providers
    setProviderSwitches(prev => {
      const newSwitches = { ...prev };

      Object.keys(allUpdates).forEach(id => {
        if (newSwitches[id]) {
          newSwitches[id] = {
            ...newSwitches[id],
            ...allUpdates[id]
          };
        }
      });

      return newSwitches;
    });

    // Send a single update with all changes using the new values directly
    await updateSeriesWithCompleteState(allUpdates);
  };  // Handler for use storage changes
  const handleUseStorageChange = async (providerId: string, enabled: boolean) => {
    // Build the update for this provider
    const switchUpdate = { [providerId]: { useStorage: enabled } };

    // Update state
    setProviderSwitches(prev => {
      const newSwitches = { ...prev };
      if (newSwitches[providerId]) {
        newSwitches[providerId] = {
          useTitle: newSwitches[providerId].useTitle,
          useCover: newSwitches[providerId].useCover,
          useStorage: enabled
        };
      }
      return newSwitches;
    });

    // Send update with the new value directly
    await updateSeriesWithCompleteState(switchUpdate);
  };  // Handler for provider disabled state changes
  const handleDisabledChange = (providerId: string, disabled: boolean) => {    setProviderDisabledStates(prev => ({
      ...prev,
      [providerId]: disabled
    }));

    // Trigger immediate update with explicit disabled state
    updateSeriesWithDisabledState(providerId, disabled);
  };  // Handler for provider deletion
  const handleDeleteProvider = (providerId: string) => {
    setProviderDeletedStates(prev => ({
      ...prev,
      [providerId]: true
    }));    // Also set the provider as disabled
    setProviderDisabledStates(prev => ({
      ...prev,
      [providerId]: true
    }));

    // Trigger immediate update with explicit deleted state
    updateSeriesWithDeletedState(providerId, true);  };
  // Handler for fromChapter changes
  const handleFromChapterChange = (providerId: string, value: string) => {
    
    // Create the updated fromChapters object
    const updatedFromChapters = {
      ...providerFromChapters,
      [providerId]: value
    };
    
    // Update state
    setProviderFromChapters(updatedFromChapters);
    
    // We need to modify the updateSeries function to handle explicit fromChapter values
    updateSeriesWithFromChapter(providerId, value);
  };  // Handler for pause downloads toggle
  const handlePausedDownloadsToggle = () => {
    // Calculate new value
    const newPausedDownloadsValue = !pausedDownloads;
    
    // Update state
    setPausedDownloads(newPausedDownloadsValue);
    
    // We need to modify the updateSeries function to handle explicit pausedDownloads value
    updateSeriesWithPausedDownloads(newPausedDownloadsValue);
  };

  // Handler for delete series button click
  const handleDeleteSeriesClick = () => {
    setShowDeleteDialog(true);
  };

  // Handler for delete series confirmation
  const handleDeleteSeriesConfirm = async () => {
    if (!seriesId) return;
    
    try {
      // Set deleting state to prevent further queries and polling
      setIsDeleting(true);
      
      // Cancel all ongoing queries for this series to prevent loops
      await queryClient.cancelQueries({ queryKey: ['series', 'detail', seriesId] });
      await queryClient.cancelQueries({ queryKey: ['downloads', 'series', seriesId] });
      
      await deleteSeries.mutateAsync({ 
        id: seriesId, 
        alsoPhysical: deletePhysicalFiles 
      });
      
      // Navigate back to library after successful deletion
      router.push('/library');
    } catch (error) {
      console.error('Failed to delete series:', error);
      // Reset deleting state on error so user can try again
      setIsDeleting(false);
    } finally {
      setShowDeleteDialog(false);
      setDeletePhysicalFiles(false); // Reset switch for next use
    }
  };

  // Handler for delete series cancellation
  const handleDeleteSeriesCancel = () => {
    setShowDeleteDialog(false);
    setDeletePhysicalFiles(false); // Reset switch for next use
  };

  // Handler for verify integrity button click
  const handleVerifyIntegrityClick = async () => {
    if (!seriesId) return;
    
    try {
      
      const result = await verifyIntegrity.mutateAsync(seriesId);
      setVerifyResult(result);
      
      if (result.success) {
        // Show success dialog
        setShowVerifyDialog(true);
      } else {
        // Show cleanup dialog with problematic files
        setShowCleanupDialog(true);
      }
    } catch (error) {
      console.error('Failed to verify integrity:', error);
    }
  };

  // Handler for verify success dialog close
  const handleVerifyDialogClose = async () => {
    setShowVerifyDialog(false);
    setVerifyResult(null);
    
    // Refresh the series data after successful verification
    if (seriesId) {
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'detail', seriesId] 
      });
    }
  };

  // Handler for cleanup dialog cancel
  const handleCleanupCancel = () => {
    setShowCleanupDialog(false);
    setVerifyResult(null);
  };

  // Handler for cleanup confirmation
  const handleCleanupConfirm = async () => {
    if (!seriesId) return;
    
    try {
      
      await cleanupSeries.mutateAsync(seriesId);
      
      
      // Refresh the series data after cleanup
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'detail', seriesId] 
      });
      
    } catch (error) {
      console.error('Failed to cleanup series:', error);
    } finally {
      setShowCleanupDialog(false);
      setVerifyResult(null);
    }
  };

  // Helper function to get status display text for archive results
  const getArchiveResultDisplay = (result: ArchiveResult) => {
    switch (result) {
      case ArchiveResult.Fine:
        return { text: 'Fine', color: 'text-green-600' };
      case ArchiveResult.NotAnArchive:
        return { text: 'Not an Archive', color: 'text-red-600' };
      case ArchiveResult.NoImages:
        return { text: 'No Images', color: 'text-yellow-600' };
      case ArchiveResult.NotFound:
        return { text: 'Not Found', color: 'text-red-600' };
      default:
        return { text: 'Unknown', color: 'text-gray-600' };
    }
  };

  // Helper function to update with explicit fromChapter value
  const updateSeriesWithFromChapter = async (providerId: string, fromChapterValue: string) => {
    if (!series) return;
    
    try {
      // Create a temporary modified fromChapters object with the new value
      const effectiveFromChapters = {
        ...providerFromChapters,
        [providerId]: fromChapterValue
      };
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          // Use the modified fromChapter value for the specific provider
          const currentFromChapterValue = provider.id === providerId ? 
            fromChapterValue : 
            providerFromChapters[provider.id];
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: currentFromChapterValue !== undefined ? parseFloat(currentFromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          
          
          return updatedProvider;
        })
      };

      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend
      // This ensures that if the backend changed any values (like fromChapter), the UI reflects them
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
        });
        
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }
      
      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with fromChapter:', error);
    }
  };

  const updateSeriesWithCompleteState = async (overrideSwitches?: Record<string, { useTitle?: boolean; useCover?: boolean; useStorage?: boolean }>) => {
    if (!series) return;

    try {
      const providersToUpdate = series.providers.map(provider => {
        // Use override switches if provided, otherwise use current state
        const currentSwitches = overrideSwitches?.[provider.id] ? 
          { ...providerSwitches[provider.id], ...overrideSwitches[provider.id] } : 
          providerSwitches[provider.id];
        const currentDisabledState = providerDisabledStates[provider.id];
        const currentFromChapter = providerFromChapters[provider.id];
        
        const updatedProvider = {
          ...provider,
          useTitle: currentSwitches?.useTitle ?? provider.useTitle,
          useCover: currentSwitches?.useCover ?? provider.useCover,
          isStorage: currentSwitches?.useStorage ?? provider.isStorage,
          isDisabled: currentDisabledState ?? provider.isDisabled,
          fromChapter: currentFromChapter !== undefined ? 
            (currentFromChapter === "" ? undefined : parseInt(currentFromChapter)) : 
            provider.fromChapter
        };
        return updatedProvider;
      });

      const updatedSeries = {
        ...series,
        providers: providersToUpdate,
        pausedDownloads: pausedDownloads
      };

      const result = await updateSeriesMutation.mutateAsync(updatedSeries);
      
      if (result && result.providers) {
        
        // Sync local state with backend response
        const newSwitches: Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }> = {};
        const newDisabledStates: Record<string, boolean> = {};
        const newFromChapters: Record<string, string> = {};
        
        result.providers.forEach(provider => {
          newSwitches[provider.id] = {
            useTitle: provider.useTitle,
            useCover: provider.useCover,
            useStorage: provider.isStorage
          };
          newDisabledStates[provider.id] = provider.isDisabled;
          newFromChapters[provider.id] = provider.fromChapter?.toString() || "";
        });
        
        setProviderSwitches(newSwitches);
        setProviderDisabledStates(newDisabledStates);
        setProviderFromChapters(newFromChapters);
      } else {
        console.error(`Failed to update series with complete state: no result received`);
      }
    } catch (error) {
      console.error(`Error updating series with complete state:`, error);
    }
  };
  
  // Handle error states first, before accessing series
  if (!seriesId) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-lg text-red-500">Series ID is required</div>
      </div>
    );
  }

  if (isDeleting) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-lg">Deleting series...</div>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-lg">Loading series details...</div>
      </div>
    );
  }

  if (error || !series) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-lg text-red-500">
          {error ? "Error loading series" : "Series not found"}
        </div>
      </div>
    );
  }
  
  // Derive ExistingSource array from providers for Add Sources mode
  const existingSources: ExistingSource[] = series.providers
    .filter((provider: any) => !providerDeletedStates[provider.id]) // Filter out deleted providers
    .map((provider: any) => ({
      provider: provider.provider,
      scanlator: provider.scanlator,
      lang: provider.lang
    }));

  // Count of non-deleted providers
  const visibleProvidersCount = series.providers.filter(provider => !providerDeletedStates[provider.id]).length;

  return (<>
    {/* Three-area layout */}
    <div className="grid grid-cols-1 lg:grid-cols-5 gap-3 h-full w-full overflow-hidden">          {/* Left Column - Two rows (80% width) */}
      <div className="lg:col-span-4 space-y-3 min-w-0 overflow-hidden">
        {/* Top Left: Series Details */}          <Card className="bg-secondary overflow-hidden">
          <CardHeader className="p-4">
            <div className="flex flex-col md:flex-row gap-4 min-w-0 overflow-hidden">
              {/* Poster */}
              <div className="flex-shrink-0 mx-auto md:mx-0">
                <img src={displayThumbnail || "/kaizoku.net.png"}
                  alt={displayTitle}
                  style={{ aspectRatio: '4/6' }}
                  className="h-64 md:h-96 object-cover rounded-lg border"
                />
              </div>
              {/* Series Info */}
              <div className="flex-1 gap-2 relative flex flex-col min-w-0 overflow-hidden">
                {/* Status Badge - Top Right of Info Pane */}
                <div className="lg:absolute lg:top-0 lg:right-0 mb-2 lg:mb-0">
                  <Badge className={'text-base ' + statusDisplay.color}>
                    {statusDisplay.text}
                  </Badge>
                </div>
                <div className="min-w-0 overflow-hidden">
                  <CardTitle className="text-xl lg:text-2xl lg:pr-20 truncate">{displayTitle}</CardTitle>
                  <div className="flex flex-wrap items-center gap-2 sm:gap-3 mt-2 text-sm min-w-0">
                    <Badge variant="primary">
                      {series.chapterList}
                    </Badge>
                    {series.lastChapter && (
                      <span className="inline-flex flex-wrap items-center gap-1">
                        <span className="text-muted-foreground">
                          Last: <Badge variant="primary">{series.lastChapter}</Badge>
                        </span>
                        {series.lastChangeUTC && (
                          <span className="font-medium">
                            {new Date(series.lastChangeUTC).toLocaleDateString()} {new Date(series.lastChangeUTC).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                          </span>
                        )}

                      </span>
                    )}
                  </div>
                </div><div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-sm min-w-0 overflow-hidden">
                  {series.author && (
                    <div className="min-w-0 overflow-hidden">
                      <span className="text-muted-foreground">Author:</span>
                      <span className="ml-2 font-medium truncate">{series.author}</span>
                    </div>
                  )}
                  {series.artist && (
                    <div className="min-w-0 overflow-hidden">
                      <span className="text-muted-foreground">Artist:</span>
                      <span className="ml-2 font-medium truncate">{series.artist}</span>
                    </div>
                  )}
                </div>

                {series.genre && series.genre.length > 0 && (
                  <div className="flex flex-wrap gap-1 mt-1 min-w-0 overflow-hidden">
                    {series.genre.map((genre) => (
                      <Badge key={genre} variant="primary" className="text-sm flex-shrink-0">
                        {genre}
                      </Badge>
                    ))}
                  </div>
                )}

                  {/* Description - Flexible area that fills available space */}
                  {series.description && (
                    <div className="flex-1 min-w-0 overflow-hidden">
                      <p className="text-sm mt-1 break-words">{series.description}</p>
                    </div>
                  )}

                  {/* Series Path Display - Bottom of info section */}
                  {series.path && (
                    <div className="mt-auto min-w-0 overflow-hidden">
                      <div
                      className="bg-background border border-input rounded-md px-3 py-2 text-sm font-mono text-muted-foreground break-all shadow-sm w-full overflow-hidden"
                      >
                      {series.path}
                      </div>
                    </div>
                  )}
                
                {/* Action Buttons - Delete, Verify, and Pause/Resume Downloads */}
                <div className="mt-auto pt-2 lg:absolute lg:bottom-0 lg:right-0 flex flex-wrap gap-2 justify-center md:justify-end">
                  {/* Delete Series Button */}
                  <Button
                    variant="destructive"
                    size="sm"
                    onClick={handleDeleteSeriesClick}
                  >
                    <Trash2 className="h-4 w-4 mr-1" />
                    <span className="hidden sm:inline">Delete Series</span>
                    <span className="sm:hidden">Delete</span>
                  </Button>

                  {/* Verify Integrity Button */}
                  <Button
                    variant="default"
                    size="sm"
                    onClick={handleVerifyIntegrityClick}
                    disabled={verifyIntegrity.isPending}
                  >
                    <ShieldCheck className="h-4 w-4 mr-1" />
                    {verifyIntegrity.isPending ? "..." : "Verify"}
                  </Button>

                  {/* Pause/Resume Downloads Button */}
                  <Button
                    variant={pausedDownloads ? "default" : "destructive"}
                    size="sm"
                    onClick={handlePausedDownloadsToggle}
                    className="flex items-center gap-2"
                  >
                    {pausedDownloads ? (
                      <>
                        <Play className="h-4 w-4" />
                        <span className="hidden sm:inline">Resume Downloads</span>
                        <span className="sm:hidden">Resume</span>
                      </>
                    ) : (
                      <>
                        <Pause className="h-4 w-4" />
                        <span className="hidden sm:inline">Pause Downloads</span>
                        <span className="sm:hidden">Pause</span>
                      </>
                    )}
                  </Button>
                </div>
              </div>
            </div>            </CardHeader>
        </Card>          {/* Bottom Left: Providers */}
        <Card className="overflow-hidden">
          <CardHeader className="p-4 pb-0">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <CardTitle className="flex items-center gap-2">
                Sources
                <Badge variant="secondary">{visibleProvidersCount}</Badge>
              </CardTitle>
              <AddSeries
                title={displayTitle}
                existingSources={existingSources}
                seriesId={series.id}
                triggerButton={
                  <Button>
                    <Plus className="h-4 w-4 mr-2" />
                    Add Sources
                  </Button>
                }
              />
            </div>
          </CardHeader>
          <CardContent className="p-4">
            <div className="space-y-2 min-w-0 overflow-hidden">              {series.providers
            .filter(provider => !providerDeletedStates[provider.id]) // Filter out deleted providers
            .map((provider) => {
              const switches = providerSwitches[provider.id] || { useTitle: false, useCover: false, useStorage: false };
              const isDisabled = provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled);
              const currentFromChapter = providerFromChapters[provider.id] ?? provider.fromChapter?.toString() ?? "";

              // Create updated provider object with current disabled state
              const updatedProvider = {
                ...provider,
                isDisabled: isDisabled
              };

              return (                <ProviderCard
                  key={provider.id}
                  provider={updatedProvider}
                  seriesId={series.id}
                  useCover={switches.useCover}
                  useTitle={switches.useTitle}
                  useStorage={switches.useStorage}
                  fromChapter={currentFromChapter}
                  onUseCoverChange={handleUseCoverChange}
                  onUseTitleChange={handleUseTitleChange}
                  onUseStorageChange={handleUseStorageChange}
                  onDisabledChange={handleDisabledChange}
                  onDeleteProvider={handleDeleteProvider}
                  onFromChapterChange={handleFromChapterChange}
                  deletedProviderStates={providerDeletedStates}
                />
              );
            })}
          </div>
          </CardContent>
        </Card>        </div>

      {/* Right Column: Downloads (20% width) - Using new API */}
      <DownloadsPanel seriesId={series.id} isDeleting={isDeleting} />
    </div>

    {/* Delete Series Confirmation Dialog */}
    <Dialog open={showDeleteDialog} onOpenChange={setShowDeleteDialog}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Delete Series</DialogTitle>
          <DialogDescription>
            Are you sure you want to delete "{displayTitle}"? This action cannot be undone.
          </DialogDescription>
        </DialogHeader>
        
        <div className="flex items-center space-x-2 py-4">
          <Switch
            id="delete-physical-files"
            checked={deletePhysicalFiles}
            onCheckedChange={setDeletePhysicalFiles}
          />
          <Label htmlFor="delete-physical-files" className="text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70">
            Also delete Physical Files
          </Label>
        </div>
        
        <DialogFooter className="flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2">
          <Button 
            variant="outline" 
            onClick={handleDeleteSeriesCancel}
            disabled={deleteSeries.isPending}
          >
            Cancel
          </Button>
          <Button 
            variant="destructive" 
            onClick={handleDeleteSeriesConfirm}
            disabled={deleteSeries.isPending}
            className="flex items-center gap-2"
          >
            {deleteSeries.isPending ? (
              <>
                <div className="h-4 w-4 animate-spin rounded-full border-2 border-background border-t-transparent" />
                Deleting...
              </>
            ) : (
              <>
                <Trash2 className="h-4 w-4" />
                Delete
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    {/* Verify Integrity Success Dialog */}
    {showVerifyDialog && verifyResult?.success && (
      <Dialog open={showVerifyDialog} onOpenChange={setShowVerifyDialog}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-green-600" />
              Integrity Verification Complete
            </DialogTitle>
          </DialogHeader>
          
          <div className="py-4">
            <p className="text-sm text-muted-foreground">
              The integrity verification completed successfully. All files are in good condition.
            </p>
          </div>
          
          <DialogFooter className="flex justify-end gap-2">
            <Button 
              variant="default" 
              onClick={handleVerifyDialogClose}
            >
              OK
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    )}

    {/* Cleanup Confirmation Dialog - shown when verification finds issues */}
    {showCleanupDialog && verifyResult && !verifyResult.success && (
      <Dialog open={showCleanupDialog} onOpenChange={setShowCleanupDialog}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Trash2 className="h-5 w-5 text-red-600" />
              File Issues Found
            </DialogTitle>
            <DialogDescription>
              The following issues were found with the series files. You can delete these problematic files to clean up the series.
            </DialogDescription>
          </DialogHeader>
          
          <div className="py-4 max-h-60 overflow-y-auto">
            <div className="space-y-2">
              {verifyResult?.badFiles?.map((result: any, index: number) => {
                const displayResult = getArchiveResultDisplay(result.result);
                return (
                  <div key={index} className="flex items-center gap-2 p-2 bg-secondary rounded">
                    <div className={`h-6 w-6 rounded-full flex items-center justify-center bg-red-100`}>
                      <div className="h-2.5 w-2.5 rounded-full bg-red-600" />
                    </div>
                    <div className="text-sm flex-1">
                      <div className="font-medium truncate" title={result.filename}>
                        {result.filename}
                      </div>
                      <div className={`text-xs ${displayResult.color}`}>
                        {displayResult.text}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
          
          <DialogFooter className="flex flex-col sm:flex-row sm:justify-end sm:space-x-2">
            <Button 
              variant="outline" 
              onClick={handleCleanupCancel}
            >
              Cancel
            </Button>
            <Button 
              variant="destructive" 
              onClick={handleCleanupConfirm}
              disabled={cleanupSeries.isPending}
              className="flex items-center gap-2"
            >
              <Trash2 className="h-4 w-4" />
              {cleanupSeries.isPending ? "Cleaning..." : "Delete Files"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    )}
  </>  );
}
