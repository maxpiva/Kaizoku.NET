"use client";

import React from 'react';
import {
  Dialog,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from '@/components/ui/dialog';
import {
  Drawer,
  DrawerContent,
  DrawerHeader,
  DrawerTitle,
  DrawerDescription,
  DrawerFooter,
} from '@/components/ui/drawer';
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
import { useMediaQuery } from "@/hooks/use-media-query";

interface CloudLatestDetailsModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  item: LatestSeriesInfo;
  onAddSeries?: () => void;
}

export const CloudLatestDetailsModal: React.FC<CloudLatestDetailsModalProps> = ({
  open,
  onOpenChange,
  item,
  onAddSeries,
}) => {
  const statusDisplay = getStatusDisplay(item.status);
  const isDesktop = useMediaQuery("(min-width: 768px)");

  const handleViewSource = () => {
    if (item.url) {
      window.open(item.url, '_blank', 'noopener,noreferrer');
    }
  };

  const handleAddSeries = () => {
    onOpenChange(false);
    onAddSeries?.();
  };

  const formatUpdatedDate = () => {
    if (!item.fetchDate) return null;
    const date = new Date(item.fetchDate);
    const month = date.toLocaleString('en-US', { month: 'short' });
    const year = date.getFullYear();
    return `Updated ${month} ${year}`;
  };

  const sourceBadge = (clickable: boolean) => (
    <span
      className={`inline-flex items-center gap-1 bg-accent text-accent-foreground rounded px-2 py-0.5 text-sm font-medium border border-border max-w-full ${
        clickable ? "cursor-pointer hover:bg-accent/80 transition-colors" : ""
      }`}
      onClick={clickable ? handleViewSource : undefined}
      title={clickable ? "Click to open in the source" : undefined}
    >
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
      <span className="truncate">{item.provider}</span>
      {clickable && <ExternalLink className="h-3 w-3 shrink-0" />}
    </span>
  );

  const byline = () => {
    const parts: string[] = [];
    if (item.author) parts.push(`by ${item.author}`);
    if (item.artist && item.artist !== item.author) parts.push(`art by ${item.artist}`);
    return parts.length > 0 ? parts.join(" · ") : null;
  };

  if (isDesktop) {
    return (
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="w-[95vw] md:max-w-[580px] p-0 overflow-hidden">
          <DialogTitle className="sr-only">{item.title}</DialogTitle>
          <DialogDescription className="sr-only">
            Details for {item.title}
          </DialogDescription>

          {/* Content area */}
          <div className="p-5 flex gap-0 items-start">
            {/* Cover wrap */}
            <div className="shrink-0 w-[120px]">
              <div className="w-[120px] aspect-[2/3] rounded-xl overflow-hidden bg-muted border border-border shadow-md relative">
                <Image
                  src={formatThumbnailUrl(item.thumbnailUrl)}
                  alt={item.title}
                  fill
                  className="object-cover"
                  onError={(e) => {
                    const target = e.target as HTMLImageElement;
                    if (target.src !== window.location.origin + '/kaizoku.net.png') {
                      target.src = '/kaizoku.net.png';
                    }
                  }}
                />
              </div>
              {/* Source badge below cover */}
              <div className="mt-2 w-full flex justify-center">
                {item.url ? sourceBadge(true) : sourceBadge(false)}
              </div>
            </div>

            {/* Metadata */}
            <div className="flex-1 min-w-0 pl-[18px]">
              {/* Title + status */}
              <div className="flex items-center gap-2 flex-wrap mb-1">
                <span className="text-[17px] font-bold tracking-tight leading-tight">
                  {item.title}
                </span>
                <Badge className={`text-xs shrink-0 ${statusDisplay.color}`}>
                  {statusDisplay.text}
                </Badge>
              </div>

              {/* Byline */}
              {byline() && (
                <div className="text-[12.5px] text-muted-foreground mb-2">
                  {byline()}
                </div>
              )}

              {/* Genre tags */}
              {item.genre && item.genre.length > 0 && (
                <div className="flex flex-wrap gap-[5px] mt-2">
                  {item.genre.map((g) => (
                    <span
                      key={g}
                      className="inline-flex items-center px-2 py-0.5 rounded-full bg-muted border border-border text-[11px] text-muted-foreground"
                    >
                      {g}
                    </span>
                  ))}
                </div>
              )}

              {/* Description */}
              <p className="text-[12.5px] text-muted-foreground leading-relaxed mt-2.5 line-clamp-4">
                {item.description || "No description available"}
              </p>

              {/* Chapter count + date */}
              <div className="mt-3 text-[11.5px] text-muted-foreground/60">
                {(item.chapterCount ?? item.latestChapter) != null && (
                  <span>{item.chapterCount ?? item.latestChapter} chapters</span>
                )}
                {(item.chapterCount ?? item.latestChapter) != null && formatUpdatedDate() && " · "}
                {formatUpdatedDate() && <span>{formatUpdatedDate()}</span>}
              </div>
            </div>
          </div>

          {/* Footer */}
          <div className="px-5 py-3 border-t border-border flex items-center justify-between bg-card/50">
            <div className="text-[11.5px] text-muted-foreground/60">
              {item.provider && <span>{item.provider}</span>}
            </div>
            <div className="flex gap-2">
              {item.url && (
                <Button
                  variant="outline"
                  className="gap-1"
                  onClick={handleViewSource}
                >
                  <ExternalLink className="h-4 w-4" />
                  View Source
                </Button>
              )}
              {item.inLibrary === InLibraryStatus.NotInLibrary && onAddSeries && (
                <Button
                  className="gap-1"
                  onClick={handleAddSeries}
                >
                  <Plus className="h-4 w-4" />
                  Add to Library
                </Button>
              )}
            </div>
          </div>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Drawer open={open} onOpenChange={onOpenChange}>
      <DrawerContent className="max-h-[92dvh] flex flex-col">
        <DrawerHeader className="sr-only">
          <DrawerTitle>{item.title}</DrawerTitle>
          <DrawerDescription>Details for {item.title}</DrawerDescription>
        </DrawerHeader>

        {/* Scrollable body */}
        <div className="overflow-y-auto flex-1" data-vaul-no-drag>
          {/* Cover section */}
          <div className="flex flex-col items-center px-5 py-3 border-b border-border">
            <div className="w-[90px] aspect-[2/3] rounded-xl overflow-hidden bg-muted border border-border shadow-md relative mb-2.5">
              <Image
                src={formatThumbnailUrl(item.thumbnailUrl)}
                alt={item.title}
                fill
                className="object-cover"
                onError={(e) => {
                  const target = e.target as HTMLImageElement;
                  if (target.src !== window.location.origin + '/kaizoku.net.png') {
                    target.src = '/kaizoku.net.png';
                  }
                }}
              />
            </div>
            <div className="text-center text-sm font-bold tracking-tight leading-tight mb-1">
              {item.title}
            </div>
            {byline() && (
              <div className="text-center text-[11px] text-muted-foreground mb-1.5">
                {byline()}
              </div>
            )}
            <div className="flex justify-center gap-1">
              <Badge className={`text-xs ${statusDisplay.color}`}>
                {statusDisplay.text}
              </Badge>
              {(item.chapterCount ?? item.latestChapter) != null && (
                <Badge variant="secondary" className="text-xs">
                  {item.chapterCount ?? item.latestChapter} chapters
                </Badge>
              )}
            </div>
          </div>

          {/* Tags section */}
          {item.genre && item.genre.length > 0 && (
            <div className="px-3.5 py-2.5 border-b border-border">
              <div className="flex flex-wrap gap-1">
                {item.genre.map((g) => (
                  <span
                    key={g}
                    className="inline-flex items-center px-2 py-0.5 rounded-full bg-muted border border-border text-[10px] text-muted-foreground"
                  >
                    {g}
                  </span>
                ))}
              </div>
            </div>
          )}

          {/* Description section */}
          <div className="px-3.5 py-2.5 border-b border-border">
            <p className="text-[11.5px] text-muted-foreground leading-relaxed">
              {item.description || "No description available"}
            </p>
          </div>

          {/* Source badges section */}
          <div className="px-3.5 py-2 border-b border-border flex gap-[5px] flex-wrap">
            {item.url ? sourceBadge(true) : sourceBadge(false)}
          </div>
        </div>

        {/* Footer */}
        <DrawerFooter className="flex-col gap-1.5 pb-[max(1rem,env(safe-area-inset-bottom))]">
          {item.inLibrary === InLibraryStatus.NotInLibrary && onAddSeries && (
            <Button
              className="w-full gap-1"
              onClick={handleAddSeries}
            >
              <Plus className="h-4 w-4" />
              Add to Library
            </Button>
          )}
          {item.url && (
            <Button
              variant="outline"
              className="w-full gap-1"
              onClick={handleViewSource}
            >
              <ExternalLink className="h-4 w-4" />
              View Source
            </Button>
          )}
        </DrawerFooter>
      </DrawerContent>
    </Drawer>
  );
};
