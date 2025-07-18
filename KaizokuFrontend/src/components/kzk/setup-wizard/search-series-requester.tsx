"use client";

import React, { useState, useEffect, useRef, useLayoutEffect, useDeferredValue } from 'react';
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MultiSelectSources } from "@/components/ui/multi-select-sources";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Drawer,
  DrawerContent,
  DrawerHeader,
  DrawerTitle,
  DrawerFooter,
} from "@/components/ui/drawer";
import { useDebounce } from "use-debounce";
import { useMediaQuery } from "@/hooks/use-media-query";
import { useSearchSeries, useAvailableSearchSources } from "@/lib/api/hooks/useSearch";
import { setupWizardService } from '@/lib/api/services/setupWizardService';
import { type LinkedSeries, type ImportInfo, type SearchSource } from "@/lib/api/types";
import Image from "next/image";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";

interface SearchSeriesRequesterProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  importTitle: string;
  importPath: string;
  onResult: (updatedImportInfo: ImportInfo) => void;
}

// Stable series card component
const SeriesCard = React.memo(({ 
  series,
  isSelected,
  onToggle,
  isDesktop 
}: { 
  series: LinkedSeries;
  isSelected: boolean;
  onToggle: (seriesId: string) => void;
  isDesktop: boolean;
}) => {
  const handleClick = React.useCallback(() => {
    onToggle(series.id);
  }, [series.id, onToggle]);

  return (
    <div
      className={`m-1 cursor-pointer transition-all duration-200 hover:shadow-lg rounded-md overflow-hidden ${
        isSelected ? 'ring-2 ring-primary shadow-md' : 'hover:ring-1 hover:ring-gray-300'
      }`}
      onClick={handleClick}
    >
      <div className="aspect-[3/4] relative">
        <Image 
          src={series.thumbnailUrl ?? '/placeholder.jpg'}
          alt={series.title || 'Series thumbnail'}
          fill
          sizes="(max-width: 768px) 50vw, (max-width: 1024px) 33vw, 20vw"
          className="object-cover"
          priority={false}
          loading="lazy"
        />
        <Badge
          variant="poster"
          className={`absolute top-1 max-w-[94%] truncate font-light ${isDesktop ? 'text-sm left-2 ' : 'text-xs left-1'}`}
        >
          {series.provider}
        </Badge>
        <div className={`absolute bottom-1 ${isDesktop ? 'right-2' : 'right-1 '}`}>
          <ReactCountryFlag
            countryCode={getCountryCodeForLanguage(series.lang)}
            svg
            style={{ 
              width: isDesktop ? '27px' : '22px', 
              height: isDesktop ? '20px' : '17px', 
              borderColor:"hsl(var(--secondary))", 
              borderWidth:"1px", 
              borderStyle:"solid"
            }}
            title={`${series.lang.toUpperCase()} (${getCountryCodeForLanguage(series.lang)})`}
          />
        </div>
      </div>
      
      <div className={`h-full p-2 text-center ${
        isSelected ? 'bg-primary text-primary-foreground' : 'bg-card'
      }`}>
        <h3 className="text-sm font-medium line-clamp-2">
          {series.title}
        </h3>
      </div>
    </div>
  );
}, (prevProps, nextProps) => {
  // Only re-render if these specific props change
  return (
    prevProps.series.id === nextProps.series.id &&
    prevProps.isSelected === nextProps.isSelected &&
    prevProps.isDesktop === nextProps.isDesktop &&
    prevProps.series.thumbnailUrl === nextProps.series.thumbnailUrl &&
    prevProps.series.title === nextProps.series.title &&
    prevProps.series.provider === nextProps.series.provider &&
    prevProps.series.lang === nextProps.series.lang
  );
});

SeriesCard.displayName = 'SeriesCard';

export interface SeriesGridHandle {
  getSelectedIds: () => string[];
}

// Memoized grid component with internal selection state and imperative handle
const SeriesGrid = React.memo(React.forwardRef<SeriesGridHandle, {
  results: LinkedSeries[];
  isDesktop: boolean;
  onSelectionCountChange: (count: number) => void;
}>(function SeriesGrid({
  results,
  isDesktop,
  onSelectionCountChange,
}, ref) {
  const [selectedSeries, setSelectedSeries] = React.useState<string[]>([]);
  const selectedSeriesSet = React.useMemo(() => new Set(selectedSeries), [selectedSeries]);

  // Expose getSelectedIds to the parent via ref
  React.useImperativeHandle(ref, () => ({
    getSelectedIds: () => selectedSeries,
  }));

  // Stable onToggle handler for each card
  const handleSeriesToggle = React.useCallback((seriesId: string) => {
    setSelectedSeries(prev => {
      const newSelection = new Set(prev);
      if (newSelection.has(seriesId)) {
        newSelection.delete(seriesId);
      } else {
        newSelection.add(seriesId);
      }
      return Array.from(newSelection);
    });
  }, []);

  // Notify parent when selection count changes
  React.useEffect(() => {
    onSelectionCountChange(selectedSeries.length);
  }, [selectedSeries, onSelectionCountChange]);

  return (
    <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-4 gap-3">
      {results.map((series) => {
        const isSelected = selectedSeriesSet.has(series.id);
        return (
          <SeriesCard
            key={series.id}
            series={series}
            isSelected={isSelected}
            onToggle={handleSeriesToggle}
            isDesktop={isDesktop}
          />
        );
      })}
    </div>
  );
}));

SeriesGrid.displayName = 'SeriesGrid';

const SearchContent = React.memo(({
  searchInputRef,
  searchValue,
  handleSearchChange,
  handleSearchFocus,
  availableSources,
  selectedSources,
  onSelectedSourcesChange,
  isDesktop,
  selectionCount,
  error,
  scrollContainerRef,
  handleScroll,
  searchResultsGrid
}: {
  searchInputRef: React.Ref<HTMLInputElement>;
  searchValue: string;
  handleSearchChange: (e: React.ChangeEvent<HTMLInputElement>) => void;
  handleSearchFocus: (e: React.FocusEvent<HTMLInputElement>) => void;
  availableSources: SearchSource[];
  selectedSources: string[];
  onSelectedSourcesChange: (sources: string[]) => void;
  isDesktop: boolean;
  selectionCount: number;
  error: string | null;
  scrollContainerRef: React.Ref<HTMLDivElement>;
  handleScroll: (e: React.UIEvent<HTMLDivElement>) => void;
  searchResultsGrid: React.ReactNode;
}) => {
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <Input
          ref={searchInputRef}
          autoFocus
          onPointerDown={(e) => e.stopPropagation()}
          type="search"
          placeholder="Search for a series..."
          className="bg-card flex-1"
          value={searchValue}
          onChange={handleSearchChange}
          onFocus={handleSearchFocus}
        />
        <div className="w-80">
          <MultiSelectSources
            sources={availableSources}
            selectedSources={selectedSources}
            onSelectionChange={onSelectedSourcesChange}
            placeholder="Select sources..."
            isDesktop={isDesktop}
          />
        </div>
        {selectionCount > 0 && (
          <div className="text-sm text-muted-foreground font-medium whitespace-nowrap">
            {selectionCount} selected
          </div>
        )}
      </div>

      {error && (
        <div className="text-sm text-destructive bg-destructive/10 p-2 rounded border">
          {error}
        </div>
      )}
      <div 
        className="h-[50vh] overflow-y-auto" 
        ref={scrollContainerRef}
        onScroll={handleScroll}
      >
        {searchResultsGrid}
      </div>
    </div>
  );
});
SearchContent.displayName = 'SearchContent';

export function SearchSeriesRequester({
  open,
  onOpenChange,
  importTitle,
  importPath,
  onResult,
}: SearchSeriesRequesterProps) {
  const [searchValue, setSearchValue] = useState("");
  const [hasUserInteracted, setHasUserInteracted] = useState(false);
  const [initialSearchDone, setInitialSearchDone] = useState(false);
  const [debouncedSearchValue] = useDebounce(searchValue, 500);
  const [selectionCount, setSelectionCount] = useState(0);
  const [gridKey, setGridKey] = useState(Date.now());
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  
  const gridRef = useRef<SeriesGridHandle>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const scrollPositionRef = useRef<number>(0); // To store scroll position
  const searchInputRef = useRef<HTMLInputElement>(null);
  
  const isDesktop = useMediaQuery("(min-width: 768px)");
  
  // Fetch available search sources
  const { data: availableSources = [] } = useAvailableSearchSources();
  
  // State for selected search sources
  const [selectedSources, setSelectedSources] = useState<string[]>([]);
  
  // Initialize selected sources when available sources are loaded
  useEffect(() => {
    if (availableSources.length > 0 && selectedSources.length === 0) {
      // Select all sources by default
      setSelectedSources(availableSources.map(source => source.sourceId));
    }
  }, [availableSources, selectedSources.length]);

  // Search when: initial search with prefilled title OR user has interacted and typed
  const shouldSearch = (!initialSearchDone && searchValue === importTitle && searchValue.length >= 3) || 
                      (hasUserInteracted && debouncedSearchValue.length >= 3);

  const { data: searchResults = [], isLoading, error: searchError, isFetching } = useSearchSeries(
    { 
      keyword: debouncedSearchValue,
      searchSources: selectedSources.length > 0 ? selectedSources : undefined
    },
    { enabled: shouldSearch }
  );

  // Mark initial search as done when we get results for the prefilled title
  useEffect(() => {
    if (searchResults.length > 0 && searchValue === importTitle && !hasUserInteracted) {
      setInitialSearchDone(true);
    }
  }, [searchResults, searchValue, importTitle, hasUserInteracted]);

  // Reset state when dialog opens
  useEffect(() => {
    if (open) {
      setSearchValue(importTitle);
      setSelectionCount(0);
      setGridKey(Date.now()); // This will reset the SeriesGrid component and its internal state
      setError(null);
      setIsSubmitting(false);
      setHasUserInteracted(false);
      setInitialSearchDone(false); // Reset initial search flag
      
      // Focus the search input and move cursor to end with multiple attempts
      const focusInput = () => {
        if (searchInputRef.current) {
          searchInputRef.current.focus();
          // Move cursor to the end of the text
          const length = searchInputRef.current.value.length;
          searchInputRef.current.setSelectionRange(length, length);
        }
      };
      
      // Try immediately
      setTimeout(focusInput, 0);
      // Try again after dialog animation
      setTimeout(focusInput, 200);
      // Final attempt after longer delay
      setTimeout(focusInput, 500);
    } else {
      // Clear search when dialog closes
      setSearchValue("");
      setSelectionCount(0);
      setHasUserInteracted(false);
      setInitialSearchDone(false);
    }
  }, [open, importTitle]);

  // Additional focus effect when searchValue updates with initial value
  useEffect(() => {
    if (open && searchValue === importTitle && !hasUserInteracted && searchInputRef.current) {
      const focusWithCursorAtEnd = () => {
        if (searchInputRef.current) {
          searchInputRef.current.focus();
          const length = searchInputRef.current.value.length;
          searchInputRef.current.setSelectionRange(length, length);
        }
      };
      
      // Try multiple times to ensure it works
      setTimeout(focusWithCursorAtEnd, 100);
      setTimeout(focusWithCursorAtEnd, 300);
    }
  }, [open, searchValue, importTitle, hasUserInteracted]);

  // Handle error from search
  useEffect(() => {
    if (searchError) {
      setError(searchError.message);
    } else {
      setError(null);
    }
  }, [searchError]);

  // Restore scroll position after re-renders
  useLayoutEffect(() => {
    if (scrollContainerRef.current) {
      scrollContainerRef.current.scrollTop = scrollPositionRef.current;
    }
  });

  const canSubmit = selectionCount > 0 && !isSubmitting;
  
  // Memoize searchResults to ensure a stable reference
  const prevResultsRef = React.useRef<LinkedSeries[]>([]);
  React.useEffect(() => {
    // Only update if the ids actually change
    const prevIds = prevResultsRef.current.map(s => s.id).join(',');
    const newIds = searchResults.map(s => s.id).join(',');
    if (prevIds !== newIds) {
      prevResultsRef.current = searchResults;
    }
  }, [searchResults]);
  const stableSearchResults = prevResultsRef.current;

  // Only reset scroll position when the search value changes (not on selection)
  useEffect(() => {
    if (scrollContainerRef.current) {
      scrollContainerRef.current.scrollTop = 0;
    }
  }, [debouncedSearchValue]);

  // Memoize the searchResultsGrid to prevent unnecessary re-renders
  const searchResultsGrid = React.useMemo(() => {
    if (isLoading || isFetching) {
      return (
        <div className="flex items-center justify-center h-full">
          <div className="text-muted-foreground">Searching...</div>
        </div>
      );
    }
    return (
      <SeriesGrid
        key={gridKey}
        ref={gridRef}
        results={stableSearchResults}
        isDesktop={isDesktop}
        onSelectionCountChange={setSelectionCount}
      />
    );
  }, [isLoading, isFetching, stableSearchResults, isDesktop, gridKey]);

  const handleSearchChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchValue(e.target.value);
    setHasUserInteracted(true);
  }, []);

  const handleSearchFocus = React.useCallback((e: React.FocusEvent<HTMLInputElement>) => {
    // Move cursor to end when focused
    const length = e.target.value.length;
    e.target.setSelectionRange(length, length);
  }, []);

  const handleScroll = React.useCallback((e: React.UIEvent<HTMLDivElement>) => {
    scrollPositionRef.current = e.currentTarget.scrollTop;
  }, []);

  const handleOk = async () => {
    const selectedIds = gridRef.current?.getSelectedIds();
    if (!selectedIds || selectedIds.length === 0) return;
    
    setIsSubmitting(true);
    setError(null);
    try {
      // Get the full LinkedSeries objects for the selected IDs
      const selectedLinkedSeries = searchResults.filter((series: LinkedSeries) => 
        selectedIds.includes(series.id)
      );
      // Call the augment endpoint
      const updatedImportInfo = await setupWizardService.augmentSeries(importPath, selectedLinkedSeries);
      // Return the result to the parent
      onResult(updatedImportInfo);
      // Close the dialog
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to augment series');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleCancel = () => {
    onOpenChange(false);
  };

  if (isDesktop) {
    return (
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent
          className="max-w-[70%]"
          onInteractOutside={(e) => {
            e.preventDefault();
          }}
        >
          <DialogHeader>
            <DialogTitle>Search Series for: {importTitle}</DialogTitle>
            <DialogDescription>
              Search for the correct series from available souces to match your local series.
            </DialogDescription>
          </DialogHeader>
          <SearchContent
            searchInputRef={searchInputRef}
            searchValue={searchValue}
            handleSearchChange={handleSearchChange}
            handleSearchFocus={handleSearchFocus}
            availableSources={availableSources}
            selectedSources={selectedSources}
            onSelectedSourcesChange={setSelectedSources}
            isDesktop={isDesktop}
            selectionCount={selectionCount}
            error={error}
            scrollContainerRef={scrollContainerRef}
            handleScroll={handleScroll}
            searchResultsGrid={searchResultsGrid}
          />
          <DialogFooter>
            <Button variant="outline" onClick={handleCancel} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button onClick={handleOk} disabled={!canSubmit}>
              {isSubmitting ? "Processing..." : "OK"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Drawer open={open} onOpenChange={onOpenChange} noBodyStyles>
      <DrawerContent>
        <DrawerHeader className="text-left">
          <DrawerTitle>Search Series for: {importTitle}</DrawerTitle>
        </DrawerHeader>
        <div className="px-4">
          <SearchContent
            searchInputRef={searchInputRef}
            searchValue={searchValue}
            handleSearchChange={handleSearchChange}
            handleSearchFocus={handleSearchFocus}
            availableSources={availableSources}
            selectedSources={selectedSources}
            onSelectedSourcesChange={setSelectedSources}
            isDesktop={isDesktop}
            selectionCount={selectionCount}
            error={error}
            scrollContainerRef={scrollContainerRef}
            handleScroll={handleScroll}
            searchResultsGrid={searchResultsGrid}
          />
        </div>
        <DrawerFooter className="pt-2">
          <Button onClick={handleOk} disabled={!canSubmit}>
            {isSubmitting ? "Processing..." : "OK"}
          </Button>
          <Button variant="outline" onClick={handleCancel} disabled={isSubmitting}>
            Cancel
          </Button>
        </DrawerFooter>
      </DrawerContent>
    </Drawer>
  );
}
