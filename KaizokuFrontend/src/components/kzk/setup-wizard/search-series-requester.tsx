"use client";

import React, { useState, useEffect, useRef } from 'react';
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
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";

const getSeriesId = (series: LinkedSeries): string => series.mihonId ?? series.providerId;

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
    onToggle(getSeriesId(series));
  }, [series, onToggle]);

  return (
    <div
      className={`m-1 cursor-pointer transition-all duration-200 hover:shadow-lg rounded-md overflow-hidden ${
        isSelected ? 'ring-2 ring-primary shadow-md' : 'hover:ring-1 hover:ring-gray-300'
      }`}
      onClick={handleClick}
    >
      <div className="aspect-[3/4] relative">
        <Image 
          src={formatThumbnailUrl(series.thumbnailUrl) ?? '/placeholder.jpg'}
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
    getSeriesId(prevProps.series) === getSeriesId(nextProps.series) &&
    prevProps.isSelected === nextProps.isSelected &&
    prevProps.isDesktop === nextProps.isDesktop &&
    prevProps.series.thumbnailUrl === nextProps.series.thumbnailUrl &&
    prevProps.series.title === nextProps.series.title &&
    prevProps.series.provider === nextProps.series.provider &&
    prevProps.series.lang === nextProps.series.lang
  );
});

SeriesCard.displayName = 'SeriesCard';

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
      <div className="h-[50vh] overflow-y-auto">
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
  const [debouncedSearchValue] = useDebounce(searchValue, 300); // Match library page debounce timing
  const [selectedSeries, setSelectedSeries] = useState<string[]>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  
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
      setSelectedSources(availableSources.map(source => source.mihonProviderId));
    }
  }, [availableSources, selectedSources.length]);

  // Simplified search condition that directly validates the debounced keyword length
  // This prevents empty keyword API calls and matches the working implementations
  const shouldSearch = debouncedSearchValue.length >= 3 && selectedSources.length > 0;

  const { data: searchResults = [], isLoading, error: searchError, isFetching } = useSearchSeries(
    { 
      keyword: debouncedSearchValue,
      searchSources: selectedSources.length > 0 ? selectedSources : undefined
    },
    { enabled: shouldSearch }
  );

  // Reset state when dialog opens
  useEffect(() => {
    if (open) {
      setSearchValue(importTitle);
      setSelectedSeries([]);
      setError(null);
      setIsSubmitting(false);
      
      // Focus the search input
      setTimeout(() => {
        if (searchInputRef.current) {
          searchInputRef.current.focus();
          const length = searchInputRef.current.value.length;
          searchInputRef.current.setSelectionRange(length, length);
        }
      }, 100);
    } else {
      // Clear search when dialog closes
      setSearchValue("");
      setSelectedSeries([]);
    }
  }, [open, importTitle]);

  // Handle error from search
  useEffect(() => {
    if (searchError) {
      setError(searchError.message);
    } else {
      setError(null);
    }
  }, [searchError]);

  const handleSeriesToggle = (seriesId: string) => {
    setSelectedSeries(prev => {
      const newSelection = new Set(prev);
      if (newSelection.has(seriesId)) {
        newSelection.delete(seriesId);
      } else {
        newSelection.add(seriesId);
      }
      return Array.from(newSelection);
    });
  };

  const canSubmit = selectedSeries.length > 0 && !isSubmitting;
  
  // Memoize the search results grid using the simple approach like the working implementation
  const searchResultsGrid = React.useMemo(() => {
    if (isLoading || isFetching) {
      return (
        <div className="flex items-center justify-center h-full">
          <div className="text-muted-foreground">Searching...</div>
        </div>
      );
    }
    
    const selectedSeriesSet = new Set(selectedSeries);
    
    return (
      <div className="grid grid-cols-3 sm:grid-cols-4 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-4 gap-3">
        {searchResults.map((series) => {
          const seriesId = series.mihonId ?? series.providerId;
          const isSelected = selectedSeriesSet.has(seriesId);
          return (
            <SeriesCard
              key={seriesId}
              series={series}
              isSelected={isSelected}
              onToggle={handleSeriesToggle}
              isDesktop={isDesktop}
            />
          );
        })}
      </div>
    );
  }, [isLoading, isFetching, searchResults, selectedSeries, isDesktop, handleSeriesToggle]);

  const handleSearchChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    setSearchValue(e.target.value);
  }, []);

  const handleSearchFocus = React.useCallback((e: React.FocusEvent<HTMLInputElement>) => {
    // Move cursor to end when focused
    const length = e.target.value.length;
    e.target.setSelectionRange(length, length);
  }, []);

  const handleOk = async () => {
    if (selectedSeries.length === 0) return;
    
    setIsSubmitting(true);
    setError(null);
    try {
      // Get the full LinkedSeries objects for the selected IDs
      const selectedLinkedSeries = searchResults.filter((series: LinkedSeries) => 
        selectedSeries.includes(getSeriesId(series))
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
            selectionCount={selectedSeries.length}
            error={error}
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
            selectionCount={selectedSeries.length}
            error={error}
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
