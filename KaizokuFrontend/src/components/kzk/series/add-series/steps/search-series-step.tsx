"use client";

import { type AddSeriesState } from "@/components/kzk/series/add-series";
import { Badge } from "@/components/ui/badge";
import { Input } from "@/components/ui/input";
import { MultiSelectSources } from "@/components/ui/multi-select-sources";
import { type LinkedSeries, type ExistingSource } from "@/lib/api/types";
import { useSearchSeries, useAvailableSearchSources } from "@/lib/api/hooks/useSearch";
import React from "react";
import { useDebounce } from "use-debounce";
import Image from "next/image";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { useMediaQuery } from "@/hooks/use-media-query";
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";
export function SearchSeriesStep({
  setError,
  setIsLoading,
  setCanProgress,
  formState,
  setFormState,
  existingSources = [],
}: {
  setError: React.Dispatch<React.SetStateAction<string | null>>;
  setIsLoading: React.Dispatch<React.SetStateAction<boolean>>;
  setCanProgress: React.Dispatch<React.SetStateAction<boolean>>;
  formState: AddSeriesState;
  setFormState: React.Dispatch<React.SetStateAction<AddSeriesState>>;
  existingSources?: ExistingSource[];
}) {
  // Component lifecycle logging
  React.useEffect(() => {return () => {};
  }, []);

  const [searchValue, setSearchValue] = React.useState(formState.searchKeyword || "");
  const [debouncedSearchValue] = useDebounce(searchValue, 500);

  // Fetch available search sources
  const { data: allAvailableSources = [] } = useAvailableSearchSources();
  
  // Use all available sources (no filtering for Add Sources mode)
  const availableSources = allAvailableSources;
  
  // State for selected search sources
  const [selectedSources, setSelectedSources] = React.useState<string[]>([]);
  // Debounce the selected sources to prevent too frequent searches when changing sources
  const [debouncedSelectedSources] = useDebounce(selectedSources, 3000);

  // Key for localStorage - make it unique for different modes
  const LOCAL_STORAGE_KEY = existingSources && existingSources.length > 0 
    ? 'kaizoku.selectedSources.addSources' 
    : 'kaizoku.selectedSources.addSeries';
  
  // Refs to track state and prevent race conditions
  const initializationState = React.useRef<{
    isInitialized: boolean;
    lastAvailableSourceIds: string[];
    hasRestoredFromStorage: boolean;
  }>({
    isInitialized: false,
    lastAvailableSourceIds: [],
    hasRestoredFromStorage: false
  });

  // Single effect to handle all initialization and updates
  React.useEffect(() => {
    if (availableSources.length === 0) return;
    
    const currentSourceIds = availableSources.map(s => s.mihonProviderId).sort();
    const state = initializationState.current;
    
    // Check if this is the first initialization
    if (!state.isInitialized) {// Try to restore from localStorage
      let restoredSources: string[] = [];
      try {
        const stored = localStorage.getItem(LOCAL_STORAGE_KEY);
        if (stored) {
          const parsed = JSON.parse(stored);
          if (Array.isArray(parsed)) {
            // Validate restored sources against current available sources
            restoredSources = parsed.filter((id): id is string => 
              typeof id === 'string' && currentSourceIds.includes(id)
            );
          }
        }
      } catch (error) {
        console.warn('[SearchSeriesStep] Failed to parse stored sources:', error);
      }
      
      // Determine what sources to select
      const sourcesToSelect = restoredSources.length > 0 ? restoredSources : currentSourceIds;// Update state
      setSelectedSources(sourcesToSelect);
      state.isInitialized = true;
      state.lastAvailableSourceIds = currentSourceIds;
      state.hasRestoredFromStorage = restoredSources.length > 0;return;
    }
    
    // Check if available sources changed (after initialization)
    const sourcesChanged = 
      currentSourceIds.length !== state.lastAvailableSourceIds.length ||
      !currentSourceIds.every((id, i) => id === state.lastAvailableSourceIds[i]);
    
    if (sourcesChanged) {// When sources change, reset to all sources
      setSelectedSources(currentSourceIds);
      state.lastAvailableSourceIds = currentSourceIds;
      state.hasRestoredFromStorage = false;
    }
  }, [availableSources]);

  // Save selection to localStorage whenever it changes (after initialization)
  React.useEffect(() => {
    if (initializationState.current.isInitialized && selectedSources.length > 0) {localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(selectedSources));
    }
  }, [selectedSources]);

  const { data: searchResults, isLoading, error, isFetching } = useSearchSeries(
    { 
      keyword: debouncedSearchValue,
      searchSources: debouncedSelectedSources.length > 0 ? debouncedSelectedSources : undefined
    },
    { enabled: debouncedSearchValue.length >= 3 && debouncedSelectedSources.length > 0 }
  );  React.useEffect(() => {
    if (searchResults) {
      setFormState(prev => {
        // Validate existing selections against new search results
        const newSearchResultIds = searchResults.map(series => series.mihonId ?? series.providerId);
        const validatedSelections = prev.selectedLinkedSeries.filter(selectedId => 
          newSearchResultIds.includes(selectedId)
        );
        
        // Log if any selections were removed
        if (validatedSelections.length !== prev.selectedLinkedSeries.length) {
          const removedSelections = prev.selectedLinkedSeries.filter(id => !newSearchResultIds.includes(id));}
        
        return {
          ...prev,
          allLinkedSeries: searchResults,
          searchKeyword: debouncedSearchValue,
          selectedLinkedSeries: validatedSelections,
        };
      });
    }
  }, [searchResults, debouncedSearchValue, setFormState]);

  React.useEffect(() => {
    // Only set loading when we're fetching and don't have any search results yet
    // Don't keep loading state active if user has already made selections
    const shouldBeLoading = (isLoading || isFetching) && !searchResults;
    setIsLoading(shouldBeLoading);
  }, [isLoading, isFetching, searchResults, setIsLoading]);

  React.useEffect(() => {
    if (error) {
      setError(error.message);
    } else {
      setError(null);
    }
  }, [error, setError]);

  React.useEffect(() => {
    // Enable progress immediately when user has made selections, regardless of loading state
    const hasSelections = formState.selectedLinkedSeries.length > 0;
    setCanProgress(hasSelections);
  }, [formState.selectedLinkedSeries, setCanProgress]);

  const getSeriesId = (series: LinkedSeries): string => series.mihonId ?? series.providerId;

  const handleSeriesToggle = (seriesId: string, checked: boolean) => {
    setFormState(prev => {
      let newSelection = [...prev.selectedLinkedSeries];
      const allSeries = prev.allLinkedSeries;
      
      if (checked) {
        // Add the clicked series
        newSelection.push(seriesId);
        
        // Only auto-select linked series if this is the first selection
        if (prev.selectedLinkedSeries.length === 0) {
          const series = allSeries.find((s: LinkedSeries) => getSeriesId(s) === seriesId);
          if (series) {
            // Add linked series automatically only on first selection
            series.linkedIds.forEach((linkedId: string) => {
              if (!newSelection.includes(linkedId)) {
                newSelection.push(linkedId);
              }
            });
          }
        }
      } else {
        // Remove only the clicked series (normal multi-select behavior)
        newSelection = newSelection.filter(id => id !== seriesId);
      }
        return {
        ...prev,
        selectedLinkedSeries: newSelection,
      };
    });
  };

  const isSeriesSelected = (seriesId: string) => {
    return formState.selectedLinkedSeries.includes(seriesId);
  };
  
  const allSeries = formState.allLinkedSeries;
  const isDesktop = useMediaQuery("(min-width: 768px)");
  return (
    <div className="mt-4 grid gap-2 rounded-md border bg-secondary p-2 sm:p-4">
      <div className="flex flex-col sm:flex-row items-stretch sm:items-center gap-2">
        <Input
          onPointerDown={(e) => e.stopPropagation()}
          type="search"
          placeholder="Search for a series..."
          className="bg-card flex-1"
          value={searchValue}
          onChange={(e) => setSearchValue(e.target.value)}
        />
        <div className="flex items-center gap-2">
          <div className="flex-1 sm:w-80 min-w-0">
            <MultiSelectSources
              sources={availableSources}
              selectedSources={selectedSources}
              onSelectionChange={(newSelection) => {setSelectedSources(newSelection);
              }}
              placeholder="Select sources..."
              isDesktop={isDesktop}
            />
          </div>
          {formState.selectedLinkedSeries.length > 0 && (
            <div className="text-sm text-muted-foreground font-medium whitespace-nowrap">
              {formState.selectedLinkedSeries.length} selected
            </div>
          )}
        </div>
      </div>
      <div className="h-[55dvh] sm:h-[60dvh] overflow-y-auto">
        <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-4 gap-2 sm:gap-3">
          {allSeries.map((series) => {
            const seriesId = getSeriesId(series);
            const isSelected = isSeriesSelected(seriesId);
            
            return (
              <div
                key={seriesId}
                className={`m-1 cursor-pointer transition-all duration-200 hover:shadow-lg rounded-md overflow-hidden ${
                  isSelected ? 'ring-2 ring-primary shadow-md' : 'hover:ring-1 hover:ring-gray-300'
                }`}
                onClick={() => handleSeriesToggle(seriesId, !isSelected)}
              >
                <div className="aspect-[3/4] relative">
                  <Image
                    src={formatThumbnailUrl(series.thumbnailUrl)}
                    alt={series.title}
                    fill
                    sizes="(max-width: 768px) 50vw, (max-width: 1024px) 33vw, 20vw"
                    className="object-cover"
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
                  </div></div>
                
                <div className={`h-full p-2 text-center ${
                  isSelected ? 'bg-primary text-primary-foreground' : 'bg-card'
                }`}>
                  <h3 className="text-sm font-medium line-clamp-2">
                    {series.title}
                  </h3>
                </div>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
}

