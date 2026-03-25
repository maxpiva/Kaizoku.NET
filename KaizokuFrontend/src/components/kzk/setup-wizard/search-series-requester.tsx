"use client";

import React, { useState, useEffect, useRef } from 'react';
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { MultiSelectSources } from "@/components/ui/multi-select-sources";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogTitle,
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
import { type LinkedSeries, type ImportInfo } from "@/lib/api/types";
import Image from "next/image";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";
import { Search } from "lucide-react";

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
          className="w-[95vw] max-w-[780px] max-h-[85vh] flex flex-col overflow-hidden p-0"
          onInteractOutside={(e) => e.preventDefault()}
        >
          {/* Header */}
          <div className="px-5 py-3.5 border-b border-border flex items-center justify-between shrink-0">
            <div>
              <DialogTitle>Search Series for: {importTitle}</DialogTitle>
              <p className="text-xs text-muted-foreground mt-0.5">
                Search for the correct series from available sources to match your local series.
              </p>
            </div>
          </div>

          {/* Search bar row */}
          <div className="flex items-center gap-2 px-5 py-3 border-b border-border shrink-0">
            <div className="relative flex-1">
              <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                ref={searchInputRef}
                autoFocus
                onPointerDown={(e) => e.stopPropagation()}
                type="search"
                placeholder="Search for a series..."
                className="bg-card h-9 pl-9"
                value={searchValue}
                onChange={handleSearchChange}
                onFocus={handleSearchFocus}
              />
            </div>
            <div className="w-72">
              <MultiSelectSources
                sources={availableSources}
                selectedSources={selectedSources}
                onSelectionChange={setSelectedSources}
                placeholder="Select sources..."
                isDesktop={isDesktop}
              />
            </div>
            <Badge variant="secondary" className="whitespace-nowrap">
              {selectedSources.length} sources
            </Badge>
          </div>

          {/* Error */}
          {error && (
            <div className="mx-5 mt-3 text-sm text-destructive bg-destructive/10 p-2 rounded border">
              {error}
            </div>
          )}

          {/* Results grid */}
          <div className="flex-1 overflow-y-auto">
            <div className="grid grid-cols-4 gap-2.5 p-4">
              {(isLoading || isFetching) ? (
                <div className="col-span-4 flex items-center justify-center py-12">
                  <div className="text-muted-foreground">Searching...</div>
                </div>
              ) : (
                searchResults.map((series) => {
                  const seriesId = getSeriesId(series);
                  return (
                    <SeriesCard
                      key={seriesId}
                      series={series}
                      isSelected={selectedSeries.includes(seriesId)}
                      onToggle={handleSeriesToggle}
                      isDesktop={isDesktop}
                    />
                  );
                })
              )}
            </div>
          </div>

          {/* Footer */}
          <div className="px-5 py-3 border-t border-border flex items-center justify-between bg-card/50 shrink-0">
            <div className="text-xs text-muted-foreground">
              <strong className="text-foreground">{selectedSeries.length}</strong> selected · {searchResults.length} results
            </div>
            <div className="flex gap-2">
              <Button variant="ghost" onClick={handleCancel} disabled={isSubmitting}>
                Cancel
              </Button>
              <Button onClick={handleOk} disabled={!canSubmit}>
                {isSubmitting ? "Processing..." : "OK"}
              </Button>
            </div>
          </div>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Drawer open={open} onOpenChange={onOpenChange}>
      <DrawerContent className="max-h-[92dvh] flex flex-col">
        {/* Header */}
        <DrawerHeader className="text-left">
          <DrawerTitle className="truncate">Search Series for: {importTitle}</DrawerTitle>
        </DrawerHeader>

        {/* Mobile search controls */}
        <div className="px-3.5 py-2 border-b border-border bg-card/50 shrink-0 space-y-1.5">
          <div className="relative">
            <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
            <Input
              ref={searchInputRef}
              autoFocus
              onPointerDown={(e) => e.stopPropagation()}
              type="search"
              placeholder="Search for a series..."
              className="bg-card h-9 pl-9 w-full"
              value={searchValue}
              onChange={handleSearchChange}
              onFocus={handleSearchFocus}
            />
          </div>
          <MultiSelectSources
            sources={availableSources}
            selectedSources={selectedSources}
            onSelectionChange={setSelectedSources}
            placeholder="Select sources..."
            isDesktop={isDesktop}
          />
        </div>

        {/* Error */}
        {error && (
          <div className="mx-3.5 mt-2 text-sm text-destructive bg-destructive/10 p-2 rounded border">
            {error}
          </div>
        )}

        {/* Results grid */}
        <div className="flex-1 overflow-y-auto" data-vaul-no-drag>
          <div className="grid grid-cols-3 gap-2 p-3">
            {(isLoading || isFetching) ? (
              <div className="col-span-2 flex items-center justify-center py-12">
                <div className="text-muted-foreground">Searching...</div>
              </div>
            ) : (
              searchResults.map((series) => {
                const seriesId = getSeriesId(series);
                return (
                  <SeriesCard
                    key={seriesId}
                    series={series}
                    isSelected={selectedSeries.includes(seriesId)}
                    onToggle={handleSeriesToggle}
                    isDesktop={isDesktop}
                  />
                );
              })
            )}
          </div>
        </div>

        {/* Footer */}
        <DrawerFooter className="flex-row gap-2 pb-[max(1rem,env(safe-area-inset-bottom))]">
          <Button variant="ghost" onClick={handleCancel} disabled={isSubmitting} className="flex-1">
            Cancel
          </Button>
          <Button onClick={handleOk} disabled={!canSubmit} className="flex-1">
            {isSubmitting ? "Processing..." : "OK"}
          </Button>
        </DrawerFooter>
      </DrawerContent>
    </Drawer>
  );
}
