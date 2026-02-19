"use client";

import React from 'react';
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogDescription,
} from '@/components/ui/dialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Plus, ExternalLink } from 'lucide-react';
import Image from 'next/image';
import { type LatestSeriesInfo, InLibraryStatus } from '@/lib/api/types';
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { DynamicTags } from "@/components/kzk/series/add-series/steps/confirm-series-step";
import { getStatusDisplay } from "@/lib/utils/series-status";
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";

interface CloudLatestDetailsModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  item: LatestSeriesInfo;
  onAddSeries: () => void;
}

export const CloudLatestDetailsModal: React.FC<CloudLatestDetailsModalProps> = ({
  open,
  onOpenChange,
  item,
  onAddSeries,
}) => {
  const statusDisplay = getStatusDisplay(item.status);

  const handleViewSource = () => {
    if (item.url) {
      window.open(item.url, '_blank', 'noopener,noreferrer');
    }
  };

  const handleAddSeries = () => {
    onOpenChange(false);
    onAddSeries();
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="w-[95vw] md:max-w-2xl p-0 overflow-hidden">
        {/* Header with title and status badge */}
        <DialogHeader className="p-3 md:p-4 pb-0">
          <div className="flex items-start justify-between gap-2 pr-10 min-w-0">
            <div className="flex flex-wrap items-center gap-2 min-w-0 flex-1">
              {item.latestChapter && (
                <Badge variant="secondary" className="shrink-0">
                  {item.latestChapter}
                </Badge>
              )}
              <DialogTitle className="text-sm md:text-base font-semibold text-primary break-words">
                {item.title}
              </DialogTitle>
            </div>
            <Badge className={`text-xs shrink-0 ${statusDisplay.color}`}>
              {statusDisplay.text}
            </Badge>
          </div>
          <DialogDescription className="sr-only">
            Details for {item.title}
          </DialogDescription>
        </DialogHeader>

        {/* Content - stacked on mobile/fold, side-by-side on md+ */}
        <div className="p-3 md:p-4 pt-2 space-y-3 min-w-0 overflow-hidden">
          <div className="flex flex-col md:flex-row gap-3 md:gap-4 min-w-0 overflow-hidden">
            {/* Thumbnail - centered on mobile, side on desktop */}
            <div className="relative mx-auto md:mx-0 shrink-0 w-[140px] md:w-32 aspect-[3/4]">
              <Image
                src={formatThumbnailUrl(item.thumbnailUrl)}
                alt={item.title}
                fill
                className="rounded-md object-cover"
                onError={(e) => {
                  const target = e.target as HTMLImageElement;
                  if (target.src !== window.location.origin + '/kaizoku.net.png') {
                    target.src = '/kaizoku.net.png';
                  }
                }}
              />
            </div>

            {/* Details */}
            <div className="flex-1 space-y-2 min-w-0 overflow-hidden">
              {/* Author/Artist */}
              {(item.author || item.artist) && (
                <div className="flex flex-wrap gap-2 text-sm text-muted-foreground">
                  {item.author && <span>by {item.author}</span>}
                  {item.artist && item.artist !== item.author && (
                    <span>art by {item.artist}</span>
                  )}
                </div>
              )}

              {/* Genre Tags */}
              {item.genre && item.genre.length > 0 && (
                <DynamicTags genres={item.genre} />
              )}

              {/* Description - scrollable */}
              <div className="max-h-28 md:max-h-32 overflow-y-auto pr-1">
                <p className="text-sm text-muted-foreground whitespace-pre-line break-words" style={{ overflowWrap: 'anywhere' }}>
                  {item.description || "No description available"}
                </p>
              </div>

              {/* Provider with country flag */}
              <div className="flex flex-wrap gap-1 pt-1">
                {item.url ? (
                  <span
                    className="inline-flex items-center gap-1 bg-accent text-accent-foreground rounded px-2 py-0.5 text-sm font-medium border border-border cursor-pointer hover:bg-accent/80 transition-colors max-w-full"
                    onClick={handleViewSource}
                    title="Click to open in the source"
                  >
                    <ExternalLink className="h-3 w-3 shrink-0" />
                    <span className="truncate">{item.provider}</span>
                    <ReactCountryFlag
                      countryCode={getCountryCodeForLanguage(item.language)}
                      svg
                      className="shrink-0"
                      style={{
                        width: '16px',
                        height: '12px',
                        borderRadius: '2px',
                        border: '1px solid #ccc',
                      }}
                      title={item.language.toUpperCase()}
                    />
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1 bg-accent text-accent-foreground rounded px-2 py-0.5 text-sm font-medium border border-border max-w-full">
                    <span className="truncate">{item.provider}</span>
                    <ReactCountryFlag
                      countryCode={getCountryCodeForLanguage(item.language)}
                      svg
                      className="shrink-0"
                      style={{
                        width: '16px',
                        height: '12px',
                        borderRadius: '2px',
                        border: '1px solid #ccc',
                      }}
                      title={item.language.toUpperCase()}
                    />
                  </span>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* Footer with action buttons */}
        <DialogFooter className="p-3 md:p-4 pt-0 flex-row gap-2 justify-end">
          {item.url && (
            <Button
              variant="outline"
              size="sm"
              className="gap-1 min-h-[44px]"
              onClick={handleViewSource}
            >
              <ExternalLink className="h-4 w-4" />
              <span className="hidden md:inline">View Source</span>
              <span className="md:hidden">Source</span>
            </Button>
          )}
          {item.inLibrary === InLibraryStatus.NotInLibrary && (
            <Button
              size="sm"
              className="gap-1 min-h-[44px]"
              onClick={handleAddSeries}
            >
              <Plus className="h-4 w-4" />
              <span className="hidden md:inline">Add to Library</span>
              <span className="md:hidden">Add</span>
            </Button>
          )}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
};
