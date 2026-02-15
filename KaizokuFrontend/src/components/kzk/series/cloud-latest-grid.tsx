"use client";

import React, { useEffect, useRef, useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { 
  Tooltip,
  TooltipContent,
  TooltipTrigger,
  TooltipProvider 
} from '@/components/ui/tooltip';
import { Plus, Loader2, Heart, ExternalLink } from 'lucide-react';
import Image from 'next/image';
import { type LatestSeriesInfo, InLibraryStatus } from '@/lib/api/types';
import { AddSeries } from '@/components/kzk/series/add-series';
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { DynamicTags } from "@/components/kzk/series/add-series/steps/confirm-series-step";
import { LastChapterBadge } from "@/components/ui/last-chapter-badge";
import { SeriesStatus } from "@/lib/api/types";
import { getStatusDisplay } from "@/lib/utils/series-status";
import { useRouter } from 'next/navigation';
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";

// Color array for the fetch date ring (31 colors from green to blue)
const FETCH_DATE_COLORS = [
  "00FF00", "22FF00", "44FF00", "66FF00", "88FF00", "AAFF00", "CCFF00", "FFFF00", 
  "FFCC00", "FFAA00", "FF8800", "FF6600", "FF4400", "FF2200", "FF0000", "FF0022", 
  "FF0044", "FF0066", "FF0088", "FF00AA", "FF00CC", "FF00FF", "CC00FF", "AA00FF", 
  "8800FF", "6600FF", "4400FF", "2200FF", "0000FF", "2200FF", "4400FF"
];

// Function to get the ring color based on days since fetch date
function getFetchDateRingColor(fetchDate?: string): string | null {
  if (!fetchDate) return null;
  
  // Get current time in browser's local timezone, then convert to UTC
  const nowLocal = new Date();
  const nowUTC = new Date(nowLocal.getTime() + (nowLocal.getTimezoneOffset() * 60000));
  
  // fetchDate is already in UTC
  const fetchUTC = new Date(fetchDate);
  
  // Calculate difference in UTC days
  const nowUTCDay = Math.floor(nowUTC.getTime() / (1000 * 60 * 60 * 24));
  const fetchUTCDay = Math.floor(fetchUTC.getTime() / (1000 * 60 * 60 * 24));
  const diffDays = nowUTCDay - fetchUTCDay;
  
  // Return color index based on days (0-30), or null if over 31 days
  if (diffDays < 0 || diffDays > 31) return null;
  const colorIndex = Math.min(diffDays, 30);
  return FETCH_DATE_COLORS[colorIndex] || null;
}

interface CloudLatestGridProps {
  items: LatestSeriesInfo[];
  isLoading: boolean;
  isLoadingMore: boolean;
  hasMore: boolean;
  onLoadMore: () => void;
  error?: Error | null;
  cardWidth: string;
  cardWidthOptions: { value: string; label: string; text: string }[];
}

interface CloudLatestCardProps {
  item: LatestSeriesInfo;
  cardWidth: string;
  textSize: string;
}

const CloudLatestCard: React.FC<CloudLatestCardProps> = ({ item, cardWidth, textSize }) => {
  const [showAddSeries, setShowAddSeries] = useState(false);
  const router = useRouter();

  // Handle card click navigation
  const handleCardClick = () => {
    if (item.seriesId) {
      // Navigate to individual series page using query parameter
      router.push(`/library/series?id=${item.seriesId}`);
    } else if (item.url) {
      // Open external URL in new tab
      window.open(item.url, '_blank', 'noopener,noreferrer');
    }
  };

  // Get ring color based on fetch date
  const ringColor = getFetchDateRingColor(item.fetchDate);
  const showRing = ringColor !== null;

  return (
    <>
      <div
        className={`relative ${cardWidth} rounded-md shadow group transition-all duration-200`}
        style={{ 
          aspectRatio: '4/6',
          ...(showRing ? {
            border: `1.5px solid #${ringColor}`,
            borderRadius: '6px',
            transition: 'all 0.2s ease-in-out',
          } : {})
        }}
        onMouseEnter={(e) => {
          if (showRing && ringColor) {
            e.currentTarget.style.borderWidth = '2.5px';
            e.currentTarget.style.boxShadow = `0 4px 20px rgba(${parseInt(ringColor.slice(0,2), 16)}, ${parseInt(ringColor.slice(2,4), 16)}, ${parseInt(ringColor.slice(4,6), 16)}, 0.3)`;
          }
        }}
        onMouseLeave={(e) => {
          if (showRing) {
            e.currentTarget.style.borderWidth = '1.5px';
            e.currentTarget.style.boxShadow = '';
          }
        }}
      >
        <Tooltip>
          <TooltipTrigger asChild>
            <div 
              className={`relative w-full h-full rounded-md overflow-hidden cursor-pointer hover:scale-105 transition-transform ${
                showRing ? 'group-hover:shadow-lg' : ''
              }`}
              onClick={handleCardClick}
            >

              <Image
                priority
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
                            {/* Provider Badge - Top Left */}
              <div className="absolute top-1 left-1 text-white text-xs font-semibold max-w-[70%] rounded shadow">
                <Badge 
                  variant="secondary" 
                  className="bg-black/70"
                >
                  {item.provider}
                </Badge>
              </div>
              
              {/* Last Chapter Badge */}
              {item.latestChapter && (
                <LastChapterBadge 
                  lastChapter={item.latestChapter}
                  status={item.status}
                />
              )}
              
              {/* In Library Heart Icon */}
              {item.inLibrary !== InLibraryStatus.NotInLibrary && (
                <div className="absolute top-7 right-1">
                  <Heart  strokeWidth={0.5} stroke='#000'
                    className={`h-8 w-8 ${
                      item.inLibrary === InLibraryStatus.InLibrary 
                        ? 'text-red-500 fill-red-500 ' 
                        : 'text-yellow-500 fill-yellow-500'
                    } drop-shadow-sm`}
                  />
                </div>
              )}
              <div className={`absolute bottom-0 left-0 w-full bg-black/60 text-white font-semibold px-2 py-1 rounded-b-md flex items-center justify-center ${textSize}`}>
                {item.title}
              </div>
            </div>
          </TooltipTrigger>
          <TooltipContent side="right" className="max-w-xl min-w-[22rem] p-0 bg-card border shadow-lg relative">
            <div className="p-4 space-y-2">
              {/* Status Badge - Top Right */}
              <div className="absolute top-4 right-4">
                <Badge className={`text-xs ${getStatusDisplay(item.status).color}`}>
                  {getStatusDisplay(item.status).text}
                </Badge>
              </div>
              
              {/* Last Chapter Badge and Title */}
              <div className="flex items-center gap-2">
                {item.latestChapter && (
                  <Badge variant="secondary" className="shrink-0"> {item.latestChapter}
                  </Badge>
                )}
                <h3 className="font-semibold text-base text-primary truncate">{item.title}</h3>
              </div>
              {(item.author || item.artist) && (
                <div className="flex flex-wrap gap-2 text-sm text-muted-foreground">
                  {item.author && <span>by {item.author}</span>}
                  {item.artist && item.artist !== item.author && <span>art by {item.artist}</span>}
                </div>
              )}
              {item.genre && item.genre.length > 0 && (
                <DynamicTags genres={item.genre} />
              )}
              <p className="text-sm text-muted-foreground line-clamp-4 whitespace-pre-line">
                {item.description || "No description available"}
              </p>
              <div className="flex flex-wrap gap-1 pt-1">
                { item.url ? (
                <span 
                  className="inline-flex items-center gap-1 bg-accent text-accent-foreground rounded px-2 py-0.5 text-sm font-medium border border-border cursor-pointer hover:bg-accent/80 transition-colors"
                  onClick={(e) => {
                    e.stopPropagation();
                    if (item.url) {
                      window.open(item.url, '_blank', 'noopener,noreferrer');
                    }
                  }}
                  title="Click to open in the source"
                ><ExternalLink className="h-3 w-3" />
                  {item.provider}
                  <ReactCountryFlag
                    countryCode={getCountryCodeForLanguage(item.language)}
                    svg
                    style={{ width: '16px', height: '12px', borderRadius: '2px', border: '1px solid #ccc' }}
                    title={item.language.toUpperCase()}
                  />
                </span>

                ) : (
                <span 
                  className="inline-flex items-center gap-1 bg-accent text-accent-foreground rounded px-2 py-0.5 text-sm font-medium border border-border">
                  {item.provider}
                  <ReactCountryFlag
                    countryCode={getCountryCodeForLanguage(item.language)}
                    svg
                    style={{ width: '16px', height: '12px', borderRadius: '2px', border: '1px solid #ccc' }}
                    title={item.language.toUpperCase()}
                  />
                </span>

                )}

              </div>
           
            </div>
            
            {/* Add Series Button - Bottom Right Absolute - Only show if not in library */}
            {item.inLibrary === InLibraryStatus.NotInLibrary && (
              <Button
                size="sm"
                className="absolute bottom-3 right-3 h-8 w-8 p-0"
                onClick={(e) => {
                  e.stopPropagation();
                  setShowAddSeries(true);
                }}
              >
                <Plus className="h-4 w-4" />
              </Button>
            )}
          </TooltipContent>
        </Tooltip>

      </div>

      {/* Add Series Modal */}
      {showAddSeries && (
        <AddSeries 
          open={showAddSeries}
          onOpenChange={setShowAddSeries}
          title={item.title}
        />
      )}
    </>
  );
};

export const CloudLatestGrid: React.FC<CloudLatestGridProps> = ({
  items,
  isLoading,
  isLoadingMore,
  hasMore,
  onLoadMore,
  error,
  cardWidth,
  cardWidthOptions
}) => {
  const observerRef = useRef<IntersectionObserver | null>(null);
  const loadMoreRef = useRef<HTMLDivElement | null>(null);
  const gridRef = useRef<HTMLDivElement>(null);
  const [columns, setColumns] = useState(1);

  // Determine text size for the card title based on cardWidth
  const textSize = cardWidthOptions.find(opt => opt.value === cardWidth)?.text || "text-sm";

  // Calculate columns on resize (same logic as main page)
  useEffect(() => {
    function updateColumns() {
      if (!gridRef.current) return;
      const containerWidth = gridRef.current.offsetWidth;
      // Dynamically measure the first card's width
      const firstItem = gridRef.current.querySelector("div.relative");
      let actualItemWidth = 272; // Default width
      if (firstItem) {
        actualItemWidth = firstItem.getBoundingClientRect().width;
      }
      // Dynamically get the computed gap from CSS
      const computedStyle = window.getComputedStyle(gridRef.current);
      let actualGap = 16;
      if (computedStyle.gap) {
        actualGap = parseFloat(computedStyle.gap);
      } else if (computedStyle.columnGap) {
        actualGap = parseFloat(computedStyle.columnGap);
      }
      const cols = Math.max(1, Math.floor((containerWidth + actualGap) / (actualItemWidth + actualGap)));
      setColumns(cols);
    }
    updateColumns();
    window.addEventListener("resize", updateColumns);
    return () => window.removeEventListener("resize", updateColumns);
  }, [items.length, cardWidth]);

  // Intersection Observer for infinite scroll
  useEffect(() => {
    if (observerRef.current) {
      observerRef.current.disconnect();
    }

    observerRef.current = new IntersectionObserver(
      (entries) => {
        const entry = entries[0];
        if (entry?.isIntersecting && hasMore && !isLoadingMore) {
          onLoadMore();
        }
      },
      { 
        threshold: 0.1,
        rootMargin: '200px' // Trigger 200px before the element becomes visible
      }
    );

    if (loadMoreRef.current) {
      observerRef.current.observe(loadMoreRef.current);
    }

    return () => {
      if (observerRef.current) {
        observerRef.current.disconnect();
      }
    };
  }, [hasMore, isLoadingMore, onLoadMore, items.length]);

  if (error) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-center">
          <p className="text-lg font-semibold text-red-600">Error loading latest series</p>
          <p className="text-sm text-muted-foreground">{error.message}</p>
        </div>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="flex items-center gap-2">
          <Loader2 className="h-6 w-6 animate-spin" />
          <span>Loading latest series...</span>
        </div>
      </div>
    );
  }

  if (items.length === 0) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-center">
          <p className="text-lg font-semibold">No series found</p>
          <p className="text-sm text-muted-foreground">Try adjusting your search or source filter</p>
        </div>
      </div>
    );
  }

  // Calculate dummy items (same logic as main page)
  const remainder = columns > 1 ? items.length % columns : 0;
  const dummyCount = remainder === 0 ? 0 : columns - remainder;

  return (
    <TooltipProvider delayDuration={2000}>
      <div className="w-full">
        <div 
          className="flex flex-wrap gap-4" 
          style={{ justifyContent: 'space-evenly' }}
          ref={gridRef}
        >
          {items.map((item, index) => (
            <CloudLatestCard 
              key={`${item.id}-${index}`} 
              item={item} 
              cardWidth={cardWidth}
              textSize={textSize}
            />
          ))}
          
          {/* Dummy items for grid alignment */}
          {Array.from({ length: dummyCount }).map((_, i) => (
            <div 
              key={`dummy-${i}`} 
              className={cardWidth} 
              style={{ 
                visibility: "hidden", 
                height: 0, 
                margin: 0, 
                padding: 0,
                maxWidth: '17rem',
                minWidth: 0
              }} 
            />
          ))}
        </div>

        {/* Load More Trigger - Outside the flex container */}
        {hasMore && (
          <div ref={loadMoreRef} className="flex items-center justify-center py-4 mt-6 min-h-[80px]">
            {isLoadingMore ? (
              <div className="flex items-center gap-2">
                <Loader2 className="h-4 w-4 animate-spin" />
                <span className="text-sm">Loading more...</span>
              </div>
            ) : (
              <div className="text-xs text-muted-foreground">Scroll to load more</div>
            )}
          </div>
        )}

        {/* End of Results */}
        {!hasMore && items.length > 0 && (
          <div className="flex items-center justify-center py-4 mt-6">
            <p className="text-sm text-muted-foreground">No more results</p>
          </div>
        )}
      </div>
    </TooltipProvider>
  );
};
