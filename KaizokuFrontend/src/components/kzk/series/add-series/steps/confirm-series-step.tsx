"use client";

import { type AddSeriesState } from "@/components/kzk/series/add-series";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";
import { type FullSeries, type ExistingSource } from "@/lib/api/types";
import React from "react";
import Image from "next/image";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { useMediaQuery } from "@/hooks/use-media-query";

// Dynamic tags component that shows tags based on available width
export function DynamicTags({ genres }: { genres: string[] }) {
  const containerRef = React.useRef<HTMLDivElement>(null);
  const [visibleCount, setVisibleCount] = React.useState<number>(6);

  React.useEffect(() => {
    if (!containerRef.current || genres.length === 0) return;

    const calculateVisibleTags = () => {
      const container = containerRef.current;
      if (!container) return;

      const containerWidth = container.offsetWidth;
      let totalWidth = 0;
      let count = 0;

      // Create temporary elements to measure tag widths
      const tempContainer = document.createElement('div');
      tempContainer.style.position = 'absolute';
      tempContainer.style.visibility = 'hidden';
      tempContainer.style.display = 'flex';
      tempContainer.style.gap = '4px';
      document.body.appendChild(tempContainer); for (const genre of genres) {
        const tempBadge = document.createElement('span');
        tempBadge.className = 'inline-flex items-center rounded-md border px-2.5 py-0.5 text-sm font-semibold transition-colors focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 border-transparent bg-secondary text-secondary-foreground hover:bg-secondary/80';
        tempBadge.textContent = genre;
        tempContainer.appendChild(tempBadge);

        const tagWidth = tempBadge.offsetWidth + 4; // Add gap

        if (totalWidth + tagWidth <= containerWidth - 80) { // Reserve space for "+X more"
          totalWidth += tagWidth;
          count++;
        } else {
          break;
        }
      }

      document.body.removeChild(tempContainer);
      setVisibleCount(Math.max(1, count)); // Show at least 1 tag
    };

    calculateVisibleTags();

    const resizeObserver = new ResizeObserver(calculateVisibleTags);
    resizeObserver.observe(containerRef.current);

    return () => resizeObserver.disconnect();
  }, [genres]);

  return (
    <div ref={containerRef} className="flex items-center gap-1 w-full">
      {genres.slice(0, visibleCount).map((genre: string) => (
        <Badge key={genre} variant="secondary" className="text-sm">
          {genre}
        </Badge>
      ))}
      {genres.length > visibleCount && (
        <Badge variant="secondary" className="text-sm">
          +{genres.length - visibleCount} more
        </Badge>
      )}
    </div>
  );
}

// Memoized SeriesCard component to prevent unnecessary re-renders
const SeriesCard = React.memo(({
  series,
  isSelected,
  isDesktop,
  onToggleSelection,
  onStorageChange,
  onCoverChange,
  onTitleChange,
}: {
  series: FullSeries;
  isSelected: boolean;
  isDesktop: boolean;
  onToggleSelection: (seriesKey: string) => void;
  onStorageChange: (seriesKey: string, checked: boolean) => void;
  onCoverChange: (seriesKey: string, checked: boolean) => void;
  onTitleChange: (seriesKey: string, checked: boolean) => void;
}) => {
  const seriesKey = `${series.id}-${series.provider}-${series.lang}-${series.scanlator}`;
  const handleCardClick = React.useCallback(() => {
    // Don't allow selection if the series is unselectable
    if (!series.isUnselectable) {
      onToggleSelection(seriesKey);
    }
  }, [seriesKey, onToggleSelection, series.isUnselectable]);

  const handleStorageClick = React.useCallback((checked: boolean) => {
    onStorageChange(seriesKey, checked);
  }, [seriesKey, onStorageChange]);

  const handleCoverClick = React.useCallback((checked: boolean) => {
    onCoverChange(seriesKey, checked);
  }, [seriesKey, onCoverChange]);

  const handleTitleClick = React.useCallback((checked: boolean) => {
    onTitleChange(seriesKey, checked);
  }, [seriesKey, onTitleChange]); return (
    <Card
      className={`overflow-hidden transition-all ${series.isUnselectable
          ? 'ml-0.5 mt-0.5 bg-gray-700/80 cursor-not-allowed opacity-75'
          : isSelected
            ? 'ml-0.5 mt-0.5 ring-primary shadow-md bg-primary/60 cursor-pointer'
            : 'ml-0.5 mt-0.5 hover:ring-1 hover:ring-gray-300 cursor-pointer'
        }`}
      onClick={handleCardClick}
    >
      <CardContent className="p-3">
        <div className="relative">
          {series.isUnselectable && (
            <Badge
              variant="destructive"
              className={`absolute max-w-[98%] truncate top-0 right-0 ${isDesktop ? 'text-xs' : 'text-[10px]'}`}
            >
              EXISTS
            </Badge>
          )}
        </div>
        <div className="flex gap-4 h-full">

          {/* Thumbnail - Poster with proper aspect ratio */}
          <div className="relative w-32 aspect-[3/4]">
            <Tooltip>
              <TooltipTrigger asChild>
                <Image
                  src={series.thumbnailUrl ?? '/placeholder.jpg'}
                  alt={series.title}
                  fill
                  sizes="(max-width: 768px) 50vw, (max-width: 1024px) 33vw, 20vw"
                  className="rounded-md object-cover cursor-pointer"
                />
              </TooltipTrigger>
              <TooltipContent side="right" className="p-0 bg-transparent border-none shadow-none">
                <div className="relative w-64 aspect-[3/4]">
                  <Image
                    src={series.thumbnailUrl ?? '/placeholder.jpg'}
                    alt={series.title}
                    fill
                    sizes="256px"
                    className="rounded-md object-cover border border-secondary"
                  />
                </div>
              </TooltipContent>
            </Tooltip>            <Badge
              variant="poster"
              className={`absolute max-w-[90%] truncate top-1 left-1 ${isDesktop ? 'text-xs left-2 ' : 'text-[10px] left-1'}`}
            >
              {series.provider}
            </Badge>

            <div className="absolute bottom-1 right-1">
              <ReactCountryFlag className="rounded-sm"
                countryCode={getCountryCodeForLanguage(series.lang)}
                svg
                style={{
                  width: '20px',
                  height: '15px',
                  borderColor: "hsl(var(--secondary))",
                  borderWidth: "1px",
                  borderStyle: "solid"
                }}
                title={`${series.lang.toUpperCase()} (${getCountryCodeForLanguage(series.lang)})`}
              />
            </div>
            {/* Scanlator Badge - Bottom Left (only if different from provider) */}
            {series.scanlator && series.scanlator !== series.provider && (
              <Badge
                variant="poster"
                className={`absolute bottom-1 left-1 text-${isDesktop ? 'text-xs left-2 ' : 'text-[10px] left-1'}`}
              >
                {series.scanlator}
              </Badge>
            )}
          </div>

          {/* Content */}
          <div className="flex-1 space-y-1 min-h-0">
            <div>
              <h3 className="font-semibold truncate align-top">{series.title}</h3>
              {(series.author || series.artist) && (
                <div className="flex justify-between items-center text-sm text-muted-foreground">
                  {series.author && (
                    <span>by {series.author}</span>
                  )}
                  {series.artist && series.artist !== series.author && (
                    <span>art by {series.artist}</span>
                  )}
                </div>
              )}
            </div>

            {/* Genre Tags - Dynamic display based on available width */}
            {series.genre.length > 0 && (
              <DynamicTags genres={series.genre} />
            )}

            {/* Description */}
            <p className="text-sm text-muted-foreground line-clamp-4">
              {series.description || "No description available"}
            </p>

            {/* Switches */}
            <div
              className="flex items-center gap-4 pt-1"
              // Prevent card selection when clicking switches
            >
              <Badge
                variant="secondary"
                className={`whitespace-nowrap text-${isDesktop ? 'text-xs' : 'text-[8px]'}`}
              >
                {series.chapterList}
              </Badge>              <div className="flex items-center space-x-2">
                <Switch
                  className={`text-${isDesktop ? 'text-xs' : 'text-[8px]'}`}
                  id={`storage-${seriesKey}`}
                  checked={series.isStorage}  onClick={(e) => e.stopPropagation()} 
                  onCheckedChange={handleStorageClick}
                  disabled={series.isUnselectable}
                />
                <Tooltip>
                  <TooltipTrigger asChild>
                    <Label onClick={(e) => e.stopPropagation()} 
                      htmlFor={`storage-${seriesKey}`}
                      className={`font-medium cursor-help text-${isDesktop ? 'text-sm' : 'text-xs'} ${series.isUnselectable ? 'opacity-50' : ''}`}
                    >
                      Use as Permanent Source
                    </Label>
                  </TooltipTrigger>
                  <TooltipContent>
                    <p><b>Permanent sources</b> always download new chapters and replace any existing copies from non-permanent sources.<br /><b>Non-permanent sources</b> only download a chapter if they are the first to have it available.</p>
                  </TooltipContent>
                </Tooltip>
              </div>
              <div className="flex items-center space-x-2">
                <Switch
                  id={`cover-${seriesKey}`}
                  checked={series.useCover} onClick={(e) => e.stopPropagation()} 
                  onCheckedChange={handleCoverClick}
                  disabled={series.isUnselectable}
                />
                <Label onClick={(e) => e.stopPropagation()} 
                  htmlFor={`cover-${seriesKey}`}
                  className={`font-medium text-${isDesktop ? 'text-sm' : 'text-xs'} ${series.isUnselectable ? 'opacity-50' : ''}`}
                >
                  Use Cover
                </Label>
              </div>
              <div className="flex items-center space-x-2">
                <Switch
                  id={`title-${seriesKey}`}
                  checked={series.useTitle} onClick={(e) => e.stopPropagation()} 
                  onCheckedChange={handleTitleClick}
                  disabled={series.isUnselectable}
                />
                <Label onClick={(e) => e.stopPropagation()} 
                  htmlFor={`title-${seriesKey}`}
                  className={`font-medium text-${isDesktop ? 'text-sm' : 'text-xs'} ${series.isUnselectable ? 'opacity-50' : ''}`}
                >
                  Use Title
                </Label>
              </div>
            </div>
          </div>
        </div>
      </CardContent>
    </Card>);
});

SeriesCard.displayName = 'SeriesCard';

// Type guard to ensure we have a valid FullSeries array
function isFullSeriesArray(value: unknown): value is FullSeries[] {
  return Array.isArray(value) && value.every(isValidFullSeries);
}

// Type guard to check if an object is a valid FullSeries
function isValidFullSeries(obj: unknown): obj is FullSeries {
  if (!obj || typeof obj !== 'object') {
    return false;
  }

  const series = obj as Record<string, unknown>;

  return (
    typeof series.id === 'string' &&
    typeof series.providerId === 'string' &&
    typeof series.provider === 'string' &&
    typeof series.scanlator === 'string' &&
    typeof series.lang === 'string' &&
    typeof series.title === 'string' &&
    typeof series.artist === 'string' &&
    typeof series.author === 'string' &&
    typeof series.description === 'string' &&
    Array.isArray(series.genre) &&
    typeof series.chapterCount === 'number' &&
    typeof series.url === 'string' &&
    typeof series.useCover === 'boolean' &&
    typeof series.isStorage === 'boolean' &&
    typeof series.useTitle === 'boolean'
  );
}

// Function to check if a FullSeries matches an ExistingSource
function isExistingSeries(series: FullSeries, existingSources: ExistingSource[]): boolean {
  return existingSources.some(existing =>
    existing.provider === series.provider &&
    existing.scanlator === series.scanlator &&
    existing.lang === series.lang
  );
}

export function ConfirmSeriesStep({
  formState,
  setFormState,
  setError: _setError,
  setIsLoading: _setIsLoading,
  setCanProgress,
  isAddSourcesMode = false,
  existingSources = [],
}: {
  formState: AddSeriesState;
  setFormState: React.Dispatch<React.SetStateAction<AddSeriesState>>;
  setError: React.Dispatch<React.SetStateAction<string | null>>;
  setIsLoading: React.Dispatch<React.SetStateAction<boolean>>;
  setCanProgress: React.Dispatch<React.SetStateAction<boolean>>;
  isAddSourcesMode?: boolean;
  existingSources?: ExistingSource[];
}) {
  // Use type guard to ensure we have properly typed data and mark existing series
  const validFullSeries: FullSeries[] = React.useMemo(() => {
    if (isFullSeriesArray(formState.fullSeries)) {
      const processedSeries = formState.fullSeries.map(series => ({
        ...series,
        isUnselectable: isExistingSeries(series, existingSources)
      }));

      // Sort so unselectable series appear first
      return processedSeries.sort((a, b) => {
        if (a.isUnselectable && !b.isUnselectable) return -1;
        if (!a.isUnselectable && b.isUnselectable) return 1;
        return 0;
      });
    }
    // If not valid, return empty array
    return [];
  }, [formState.fullSeries, existingSources]);  // Enable step navigation and finish button only when at least one series is selected
  React.useEffect(() => {
    const hasSelectedSeries = validFullSeries.some(series => series.isSelected && !series.isUnselectable);
    setCanProgress(hasSelectedSeries);
  }, [validFullSeries, setCanProgress]);

  // Initialize first non-unselectable series as selected by default
  React.useEffect(() => {
    const selectableSeries = validFullSeries.filter(series => !series.isUnselectable);
    if (selectableSeries.length > 0 && !selectableSeries.some(series => series.isSelected)) {
      setFormState((prev: AddSeriesState) => {
        const updatedSeries = prev.fullSeries.map((series: FullSeries) => {
          // Find the first selectable series and mark it as selected
          const isFirstSelectable = !series.isUnselectable &&
            selectableSeries[0] &&
            series.id === selectableSeries[0].id &&
            series.provider === selectableSeries[0].provider &&
            series.lang === selectableSeries[0].lang &&
            series.scanlator === selectableSeries[0].scanlator;

          return {
            ...series,
            isSelected: isFirstSelectable || false
          };
        });
        return {
          ...prev,
          fullSeries: updatedSeries,
        };
      });
    }
  }, [validFullSeries.length, setFormState]); const handleToggleSelection = React.useCallback((seriesKey: string) => {
    setFormState((prev: AddSeriesState) => {
      const updatedSeries = prev.fullSeries.map((series: FullSeries) => {
        const currentKey = `${series.id}-${series.provider}-${series.lang}-${series.scanlator}`;
        // Only toggle selection if series is not unselectable
        return currentKey === seriesKey && !series.isUnselectable
          ? { ...series, isSelected: !series.isSelected }
          : series;
      });
      return {
        ...prev,
        fullSeries: updatedSeries,
      };
    });
  }, [setFormState]); const handleStorageChange = React.useCallback(
    (seriesKey: string, checked: boolean): void => {
      setFormState((prev: AddSeriesState): AddSeriesState => {
        const updatedSeries: FullSeries[] = prev.fullSeries.map((series: FullSeries): FullSeries => {
          const currentKey = `${series.id}-${series.provider}-${series.lang}-${series.scanlator}`;
          return currentKey === seriesKey
            ? { ...series, isStorage: checked }
            : series;
        });
        return {
          ...prev,
          fullSeries: updatedSeries,
        };
      });
    },
    [setFormState]
  ); const handleCoverChange = React.useCallback(
    (seriesKey: string, checked: boolean): void => {
      setFormState((prev: AddSeriesState): AddSeriesState => {
        const updatedSeries: FullSeries[] = prev.fullSeries.map((series: FullSeries): FullSeries => {
          const currentKey = `${series.id}-${series.provider}-${series.lang}-${series.scanlator}`;
          return {
            ...series,
            useCover: currentKey === seriesKey
              ? checked
              : checked
                ? false
                : series.useCover,
          };
        });
        return {
          ...prev,
          fullSeries: updatedSeries,
        };
      });
    },
    [setFormState]
  ); const handleTitleChange = React.useCallback(
    (seriesKey: string, checked: boolean): void => {
      setFormState((prev: AddSeriesState): AddSeriesState => {
        const updatedSeries: FullSeries[] = prev.fullSeries.map((series: FullSeries): FullSeries => {
          const currentKey = `${series.id}-${series.provider}-${series.lang}-${series.scanlator}`;
          return {
            ...series,
            useTitle: currentKey === seriesKey
              ? checked
              : checked
                ? false
                : series.useTitle,
          };
        });
        return {
          ...prev,
          fullSeries: updatedSeries,
        };
      });
    },
    [setFormState]
  );
  // State for selected category (optional)
  const [selectedCategory, setSelectedCategory] = React.useState<string>("");  // State for editable storage path
  const [editableStoragePath, setEditableStoragePath] = React.useState<string>("");
  
  // Ref to track if category was manually changed by user
  const categoryManuallyChanged = React.useRef<boolean>(false);

  // Handler for storage path changes that updates both local state and form state
  const handleStoragePathChange = React.useCallback((newPath: string) => {
    setEditableStoragePath(newPath);
    setFormState((prev: AddSeriesState) => ({
      ...prev,
      storagePath: newPath
    }));
  }, [setFormState]);
  
  // Handler for category changes
  const handleCategoryChange = React.useCallback((newCategory: string) => {
    categoryManuallyChanged.current = true;
    setSelectedCategory(newCategory);
  }, []);// State to track if scrollbar is visible
  const [hasScrollbar, setHasScrollbar] = React.useState<boolean>(false);
  const scrollContainerRef = React.useRef<HTMLDivElement>(null);

  // Get the series that has useTitle=true
  const titleSeries = React.useMemo(() => {
    return validFullSeries.find(series => series.useTitle);
  }, [validFullSeries]);
  
  // Track the ID of the title series to detect when it actually changes
  const titleSeriesIdRef = React.useRef<string | null>(null);

  // Set initial category when titleSeries ID actually changes (not just reference)
  React.useEffect(() => {
    const currentTitleSeriesId = titleSeries ? `${titleSeries.id}-${titleSeries.provider}` : null;
    
    // Only reset category if the series ID changed or if category wasn't manually set
    if (currentTitleSeriesId !== titleSeriesIdRef.current && !categoryManuallyChanged.current) {
      titleSeriesIdRef.current = currentTitleSeriesId;
      
      if (titleSeries && titleSeries.categories && titleSeries.categories.length > 0) {
        // Use series.type if available and exists in categories, otherwise use first category
        const initialCategory = titleSeries.type && titleSeries.categories.includes(titleSeries.type)
          ? titleSeries.type
          : titleSeries.categories[0];
        setSelectedCategory(initialCategory ?? "");
      } else {
        setSelectedCategory("");
      }
    }
  }, [titleSeries]);  // Compute storage path and update editable path when dependencies change
  React.useEffect(() => {
    // If form state already has a storage path and we haven't set the editable path yet, use it
    if (formState.storagePath && !editableStoragePath) {
      setEditableStoragePath(formState.storagePath);
      return;
    }

    // Don't compute if we don't have the required data
    if (!titleSeries || !titleSeries.storageFolderPath) {
      return;
    }

    // Determine path separator from storageFolderPath
    const separator = titleSeries.storageFolderPath.includes('\\') ? '\\' : '/';

    let computedPath: string;
    if (titleSeries.categories && titleSeries.categories.length > 0 && titleSeries.useCategoriesForPath && selectedCategory) {
      // With category: storageFolderPath + separator + selectedCategory + separator + suggestedFilename
      computedPath = `${titleSeries.storageFolderPath}${separator}${selectedCategory}${separator}${titleSeries.suggestedFilename}`;
    } else {
      // Without category: storageFolderPath + separator + suggestedFilename
      computedPath = `${titleSeries.storageFolderPath}${separator}${titleSeries.suggestedFilename}`;
    }
    
    // Only update if the computed path is different from current path
    if (computedPath !== editableStoragePath) {
      handleStoragePathChange(computedPath);
    }
  }, [titleSeries, selectedCategory, formState.storagePath, editableStoragePath]);

  // Check for scrollbar visibility
  React.useEffect(() => {
    const checkScrollbar = () => {
      if (scrollContainerRef.current) {
        const hasScroll = scrollContainerRef.current.scrollHeight > scrollContainerRef.current.clientHeight;
        setHasScrollbar(hasScroll);
      }
    };

    // Check initially and after content changes
    checkScrollbar();

    // Use ResizeObserver to detect content changes
    if (scrollContainerRef.current) {
      const resizeObserver = new ResizeObserver(checkScrollbar);
      resizeObserver.observe(scrollContainerRef.current);

      return () => resizeObserver.disconnect();
    }
  }, [validFullSeries]);
  const isDesktop = useMediaQuery("(min-width: 768px)");
  return (
    <TooltipProvider>
      <div className="text-center m-0">
        <p className="text-sm text-muted-foreground">
          Review and configure your selected sources before adding them to your library.
        </p>          <p className="text-xs text-muted-foreground mt-1">
          {validFullSeries.filter(series => series.isUnselectable).length > 0 && (
            <span className="text-orange-600 font-medium">
              {validFullSeries.filter(series => series.isUnselectable).length} existing •
            </span>
          )} {validFullSeries.filter(series => series.isSelected && !series.isUnselectable).length} of {validFullSeries.filter(series => !series.isUnselectable).length} source{validFullSeries.filter(series => !series.isUnselectable).length !== 1 ? 's' : ''} selected • Click cards to select/deselect
        </p>
      </div><div className="gap-2 rounded-md border bg-secondary p-4">

        {/* Storage Path Configuration - Hidden in Add Sources mode */}
        {!isAddSourcesMode && titleSeries && (
          <div className="flex gap-4 items-end">            <div className="flex-1">
            <Label htmlFor="storage-path" className="text-sm font-medium">
              Storage Path
            </Label>              <Input
              id="storage-path"
              value={editableStoragePath}
              onChange={(e) => handleStoragePathChange(e.target.value)}
              placeholder="Enter storage path..."
              className="mt-1 bg-card mb-2"
            />
          </div>            {titleSeries.categories && titleSeries.categories.length > 0 && (
            <div className="w-48 ">
              <Label htmlFor="category-select" className="text-sm font-medium">
                Category
              </Label>
              <Select value={selectedCategory} onValueChange={handleCategoryChange}>
                <SelectTrigger id="category-select" className="mt-1 bg-card mb-2">
                  <SelectValue placeholder="Select category" />
                </SelectTrigger>
                <SelectContent className="" >
                  {titleSeries.categories.map((category) => (
                    <SelectItem key={category} value={category}>
                      {category}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}
          </div>
        )}      <div
          ref={scrollContainerRef}
          className={`h-[60dvh] overflow-y-auto space-y-4 ${hasScrollbar ? 'pr-2' : ''}`}      >        {validFullSeries.map((series: FullSeries) => (
            <SeriesCard
              key={`${series.id}-${series.provider}-${series.lang}-${series.scanlator}`}
              series={series}
              isSelected={series.isSelected || false}
              isDesktop={isDesktop}
              onToggleSelection={handleToggleSelection}
              onStorageChange={handleStorageChange}
              onCoverChange={handleCoverChange}
              onTitleChange={handleTitleChange}
            />
          ))}
        </div>
      </div>
    </TooltipProvider>
  );
}
