"use client";

import { useLibrary } from "@/lib/api/hooks/useSeries";
import { useSearch } from "@/contexts/search-context";
import Image from "next/image";
import { useRouter } from "next/navigation";
import { useEffect, useMemo, useRef, useState } from "react";
import { type SeriesInfo, SeriesStatus } from "@/lib/api/types";
import "./list-series.css";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { LastChapterBadge } from "@/components/ui/last-chapter-badge";
import { DynamicTags } from "@/components/kzk/series/add-series/steps/confirm-series-step";
import { Badge } from "@/components/ui/badge";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { Database, ExternalLink } from "lucide-react";
import { getStatusDisplay } from "@/lib/utils/series-status";
// Color array for the last change ring (31 colors from green to blue)
const LAST_CHANGE_COLORS = [
  "00FF00", "22FF00", "44FF00", "66FF00", "88FF00", "AAFF00", "CCFF00", "FFFF00",
  "FFCC00", "FFAA00", "FF8800", "FF6600", "FF4400", "FF2200", "FF0000", "FF0022",
  "FF0044", "FF0066", "FF0088", "FF00AA", "FF00CC", "FF00FF", "CC00FF", "AA00FF",
  "8800FF", "6600FF", "4400FF", "2200FF", "0000FF", "2200FF", "4400FF"
];

// Function to get the ring color based on days since last change
function getLastChangeRingColor(lastChangeUTC?: string): string | null {
  if (!lastChangeUTC) return null;

  const now = new Date();
  const lastChange = new Date(lastChangeUTC);
  const diffTime = now.getTime() - lastChange.getTime();
  const diffDays = Math.floor(diffTime / (1000 * 60 * 60 * 24));

  // Return color index based on days (0-30), or null if over 31 days
  if (diffDays < 0 || diffDays > 31) return null;
  const colorIndex = Math.min(diffDays, 30);
  return LAST_CHANGE_COLORS[colorIndex] || null;
}

export interface ListSeriesProps {
  filterFn?: (series: SeriesInfo) => boolean;
  sortFn?: (a: SeriesInfo, b: SeriesInfo) => number;
  cardWidth?: string;
  cardWidthOptions?: { value: string; label: string; text: string }[];
  orderBy?: string;
  library?: SeriesInfo[];
}

export function ListSeries({ filterFn, sortFn, cardWidth = "w-40", cardWidthOptions, orderBy, library: propLibrary }: ListSeriesProps) {
  // Always call hooks at the top level to avoid hook count mismatches
  const { data: hookLibrary, isLoading } = useLibrary();
  const { debouncedSearchTerm } = useSearch();
  const router = useRouter();
  const gridRef = useRef<HTMLDivElement>(null);
  const [columns, setColumns] = useState(1);
  const itemWidth = 272; // px (17rem + 1rem gap, adjust as needed)
  const gap = 16; // px (1rem)

  // Use prop library if provided, otherwise use hook library
  const library = propLibrary ?? hookLibrary;

  // Filter and sort series based on debounced search term, filterFn, and sortFn
  const filteredLibrary = useMemo(() => {
    if (!library) return library;
    let result = library;
    if (debouncedSearchTerm.trim()) {
      result = result.filter((series: SeriesInfo) =>
        series.title.toLowerCase().includes(debouncedSearchTerm.toLowerCase())
      );
    }
    if (filterFn) {
      result = result.filter(filterFn);
    }
    if (sortFn) {
      result = [...result].sort(sortFn);
    }
    return result;
  }, [library, debouncedSearchTerm, filterFn, sortFn]);

  // Calculate columns on resize
  useEffect(() => {
    function updateColumns() {
      if (!gridRef.current) return;
      const containerWidth = gridRef.current.offsetWidth;
      // Dynamically measure the first card's width
      const firstItem = gridRef.current.querySelector("div.relative");
      let actualItemWidth = itemWidth;
      if (firstItem) {
        actualItemWidth = firstItem.getBoundingClientRect().width;
      }
      // Dynamically get the computed gap from CSS
      const computedStyle = window.getComputedStyle(gridRef.current);
      // Try 'gap' first, fallback to 'columnGap' for cross-browser
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
  }, [filteredLibrary?.length, cardWidth]);
  // Determine text size for the card title based on cardWidth
  const textSize = cardWidthOptions?.find(opt => opt.value === cardWidth)?.text || "text-sm";
  
  const handleSeriesClick = (seriesId: string) => {
    router.push(`/library/series?id=${seriesId}`);
  };

  // Calculate dummy items and remainder for layout
  const items = filteredLibrary || [];
  const remainder = columns > 1 ? items.length % columns : 0;
  const dummyCount = remainder === 0 ? 0 : columns - remainder;

  return (
    <TooltipProvider delayDuration={2000}>
      <div className="flex flex-wrap gap-4 grid-auto-fit" ref={gridRef}>
        {isLoading ? (
          <div>Loading...</div>
        ) : filteredLibrary === undefined || filteredLibrary?.length === 0 ? (
          <div>
            {debouncedSearchTerm.trim() ? `No series found matching "${debouncedSearchTerm}"` : 'No series found'}
          </div>
        ) : (
          <>
            {items.map((series: SeriesInfo) => {
        const ringColor = getLastChangeRingColor(series.lastChangeUTC);
        const showRing = orderBy === "lastChange" && ringColor; return (
          <div
            key={series.id}
            className={`relative ${cardWidth} rounded-md shadow group transition-all duration-200`} style={{
              aspectRatio: '4/6',
              ...(showRing ? {
                border: `1.5px solid #${ringColor}`,
                borderRadius: '6px',
                transition: 'all 0.2s ease-in-out',
              } : {})
            }}
            onMouseEnter={(e) => {
              if (showRing) {
                e.currentTarget.style.borderWidth = '2.5px';
                e.currentTarget.style.boxShadow = `0 4px 20px rgba(${parseInt(ringColor.slice(0, 2), 16)}, ${parseInt(ringColor.slice(2, 4), 16)}, ${parseInt(ringColor.slice(4, 6), 16)}, 0.3)`;
              }
            }}
            onMouseLeave={(e) => {
              if (showRing) {
                e.currentTarget.style.borderWidth = '1.5px';
                e.currentTarget.style.boxShadow = '';
              }
            }}
          ><Tooltip>              <TooltipTrigger asChild>
            <div
              className={`relative w-full h-full rounded-md overflow-hidden cursor-pointer hover:scale-105 transition-transform ${showRing ? 'group-hover:shadow-lg' : ''
                }`}
              onClick={() => handleSeriesClick(series.id)}
              style={{
                ...(showRing ? {
                  transition: 'all 0.2s ease-in-out',
                } : {})
              }}
            ><Image
                priority
                src={series.thumbnailUrl ?? '/placeholder.jpg'}
                alt={series.title}
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
                                {series.lastChangeProvider.provider}
                              </Badge>
                            </div>
              <div className={`absolute bottom-0 left-0 w-full bg-black/60 text-white font-semibold px-2 py-1 rounded-b-md flex items-center justify-center ${textSize}`}>
                {series.title}
              </div>
            </div>
          </TooltipTrigger>
              <TooltipContent side="right" className="max-w-xl min-w-[22rem] p-0 bg-card border shadow-lg">
                <div className="p-4 space-y-2">
                  {/* Status Badge - Top Right */}
                  <div className="absolute top-4 right-4">
                    <Badge className={`text-xs ${getStatusDisplay(series.status).color}`}>
                      {getStatusDisplay(series.status).text}
                    </Badge>
                  </div>
                  <div className="flex items-center gap-2">
                    {(series.lastChapter) && (
                      <Badge variant="secondary" className="shrink-0"> {series.lastChapter}
                      </Badge>
                    )}
                    <h3 className="font-semibold text-base text-primary truncate">{series.title}</h3>
                  </div>
                  {(series.author || series.artist) && (
                    <div className="flex flex-wrap gap-2 text-sm text-muted-foreground">
                      {series.author && <span>by {series.author}</span>}
                      {series.artist && series.artist !== series.author && <span>art by {series.artist}</span>}
                    </div>
                  )}
                  {series.genre && series.genre.length > 0 && (
                    <DynamicTags genres={series.genre} />
                  )}
                  <p className="text-sm text-muted-foreground line-clamp-4 whitespace-pre-line">
                    {series.description || "No description available"}
                  </p>
                  {series.providers && series.providers.length > 0 && (
                    <div className="flex flex-wrap gap-1 pt-1">
                      {series.providers.map((provider, index) => (
                        provider.url ? (
                          <span key={`${provider.provider}-${provider.language}-${provider.scanlator || 'no-scanlator'}-${index}`} className="inline-flex items-center gap-1 bg-accent text-accent-foreground rounded px-2 py-0.5 text-sm font-medium border border-border cursor-pointer hover:bg-accent/80 transition-colors"
                            onClick={(e) => {
                              e.stopPropagation();
                              if (provider.url) {
                                window.open(provider.url, '_blank', 'noopener,noreferrer');
                              }
                            }}
                            title="Click to open in the source"
                          >
                            <ExternalLink className="h-3 w-3" />
                            {provider.provider}{provider.scanlator && provider.scanlator !== provider.provider && ` • ${provider.scanlator}`}
                            <ReactCountryFlag
                              countryCode={getCountryCodeForLanguage(provider.language)}
                              svg
                              style={{ width: '16px', height: '12px', borderRadius: '2px', border: '1px solid #ccc' }}
                              title={provider.language.toUpperCase()}
                            />
                            {(provider.isStorage) && (
                              <Database className="h-3.5 w-3.5 stroke-green-500" />
                            )}
                          </span>
                        ) : (
                          <span key={`${provider.provider}-${provider.language}-${provider.scanlator || 'no-scanlator'}-${index}`} className="inline-flex items-center gap-1 bg-accent text-accent-foreground rounded px-2 py-0.5 text-sm font-medium border border-border">
                            {provider.provider}{provider.scanlator && provider.scanlator !== provider.provider && ` • ${provider.scanlator}`}
                            <ReactCountryFlag
                              countryCode={getCountryCodeForLanguage(provider.language)}
                              svg
                              style={{ width: '16px', height: '12px', borderRadius: '2px', border: '1px solid #ccc' }}
                              title={provider.language.toUpperCase()}
                            />
                            {(provider.isStorage) && (
                              <Database className="h-3.5 w-3.5 stroke-green-500" />
                            )}
                          </span>
                        )
                      ))}
                    </div>
                  )}
                </div>
              </TooltipContent>            </Tooltip>
            {/* Last Chapter Badge */}            {series.lastChapter !== undefined && (
              <LastChapterBadge
                lastChapter={series.lastChapter}
                status={series.isActive ? series.status : SeriesStatus.DISABLED}
              />)}
          </div>
        );
      })}
            {Array.from({ length: dummyCount }).map((_, i) => (
              <div key={`dummy-${i}`} className={cardWidth} style={{ visibility: "hidden", height: 0, margin: 0, padding: 0 }} />
            ))}
          </>
        )}
      </div>
    </TooltipProvider>
  );
}
