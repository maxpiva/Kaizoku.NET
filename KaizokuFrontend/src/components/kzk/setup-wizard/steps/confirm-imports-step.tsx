/*
 * Simplified Import State Management:
 * 
 * 1. Load imports from API on component mount
 * 2. Store in local state - no backend sync
 * 3. Render imports using tabs as filters
 * 4. On any user interaction, update the local model directly
 * 5. All UI updates are immediate and local
 * 
 * No backend synchronization, no cross-component wiring.
 */

"use client";

import React, { useState, useEffect, useRef, useMemo, useCallback, useContext, createContext } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Button } from "@/components/ui/button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import ReactCountryFlag from "react-country-flag";
import { LazyImage } from "@/components/ui/lazy-image";
import { Search, X, Plus, ExternalLink } from "lucide-react";
import { useSetupWizardImports, useSetupWizardImportSeries, useSetupWizardUpdateImport } from "@/lib/api/hooks/useSetupWizard";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { SearchSeriesRequester } from "@/components/kzk/setup-wizard/search-series-requester";
import type { ImportInfo, SmallSeries } from "@/lib/api/types";
import { ImportStatus, Action } from "@/lib/api/types";
import { VariableSizeList as List } from 'react-window';
import AutoSizer from 'react-virtualized-auto-sizer';
import Image from "next/image";

// --- Imports Context for global state updates ---
interface ImportsContextType {
  updateImportField: (path: string, field: string, value: any, seriesIndex?: number) => void;
}
const ImportsContext = createContext<ImportsContextType | undefined>(undefined);

// Virtualized list component for ImportCards
interface VirtualizedImportListProps {
  items: ImportInfo[];
  onStatusChange: (path: string, status: ImportStatus) => void;
  onActionChange: (path: string, action: Action) => void;
  onProviderToggle: (path: string, seriesIndex: number) => void;
  onChapterChange: (path: string, chapter: number) => void;
  onSeriesPropertyChange: (path: string, seriesIndex: number, property: 'useCover' | 'isStorage' | 'useTitle', value: boolean) => void;
  showActionCombobox?: boolean;
  isUpdating?: boolean;
  showSearchButton?: boolean;
  showSkipButton?: boolean;
  showAddButton?: boolean;
  onImportUpdate?: (updatedImport: ImportInfo) => void;
  onButtonUpdate?: (updatedImport: ImportInfo) => void;
}

const VirtualizedImportList = React.memo(function VirtualizedImportList({
  items,
  onStatusChange,
  onActionChange,
  onProviderToggle,
  onChapterChange,
  onSeriesPropertyChange,
  showActionCombobox = true,
  isUpdating = false,
  showSearchButton = false,
  showSkipButton = false,
  showAddButton = false,
  onImportUpdate,
  onButtonUpdate
}: VirtualizedImportListProps) {
  const listRef = useRef<List>(null);
  const itemHeights = useRef<Map<number, number>>(new Map());
  const defaultItemHeight = 400; // Estimated default height

  // Reset item heights when items change, but skip reset if only preferred changed
  const prevItemsRef = useRef(items);
  useEffect(() => {
    function onlyPreferredChanged(prevItems: ImportInfo[], newItems: ImportInfo[]): boolean {
      if (prevItems.length !== newItems.length) return false;
      for (let i = 0; i < prevItems.length; i++) {
        const prev = prevItems[i];
        const next = newItems[i];
        if (!prev || !next) return false;
        if (prev.path !== next.path) return false;
        if (prev.series && next.series) {
          for (let j = 0; j < prev.series.length; j++) {
            if (!prev.series[j] || !next.series[j]) return false;
            // Only preferred changed
            if (
              prev.series[j].id === next.series[j].id &&
              prev.series[j].title === next.series[j].title &&
              prev.series[j].provider === next.series[j].provider &&
              prev.series[j].preferred !== next.series[j].preferred
            ) {
              continue;
            }
            // If any other property changed, reset
            if (
              prev.series[j].id !== next.series[j].id ||
              prev.series[j].title !== next.series[j].title ||
              prev.series[j].provider !== next.series[j].provider
            ) {
              return false;
            }
          }
        }
      }
      return true;
    }
    if (!onlyPreferredChanged(prevItemsRef.current, items)) {
      itemHeights.current.clear();
      if (listRef.current) {
        listRef.current.resetAfterIndex(0);
      }
    }
    prevItemsRef.current = items;
  }, [items]);

  const getItemSize = (index: number) => {
    return itemHeights.current.get(index) || defaultItemHeight;
  };

  const setItemSize = (index: number, size: number) => {
    itemHeights.current.set(index, size);
    if (listRef.current) {
      listRef.current.resetAfterIndex(index);
    }
  };
  const Row = React.memo(({ index, style }: { index: number; style: React.CSSProperties }) => {
    const itemRef = useRef<HTMLDivElement>(null);
    const importItem = items[index];

    // Safety check for undefined item
    if (!importItem) {
      return <div style={style} />;
    }

    useEffect(() => {
      if (itemRef.current) {
        const observer = new ResizeObserver((entries) => {
          if (entries[0]) {
            const height = entries[0].contentRect.height;
            if (height !== getItemSize(index)) {
              setItemSize(index, height);
            }
          }
        });

        observer.observe(itemRef.current);
        return () => observer.disconnect();
      }
    }, [index]);
    // Add margin-bottom for spacing between rows
    return (
      <div style={style} className='pr-2'>
        <div ref={itemRef} >
          <ImportCard
            key={importItem.path + "|" + importItem.title}
            import={importItem}
            onStatusChange={onStatusChange}
            onActionChange={onActionChange}
            onProviderToggle={onProviderToggle}
            onChapterChange={onChapterChange}
            onSeriesPropertyChange={onSeriesPropertyChange}
            showActionCombobox={showActionCombobox}
            showSkipButton={showSkipButton}
            showSearchButton={showSearchButton}
            showAddButton={showAddButton}
            isUpdating={isUpdating}
          />
        </div>
      </div>
    );
  });

  Row.displayName = 'VirtualizedRow';
  
  // Call hook before any early returns to maintain hook order
  const { hasScrollbar, containerRef } = useScrollbarDetection();
  
  // Get appropriate empty state message
  const getEmptyStateMessage = () => {
    if (showSearchButton && showAddButton) {
      return "No series marked to skip";
    }
    if (showSkipButton) {
      return "No series marked for import";
    }
    return "No items to display";
  };

  if (items.length === 0) {
    return (
      <div className="h-[59vh] w-full flex items-center justify-center">
        <div className="text-center py-8 text-muted-foreground">
          {getEmptyStateMessage()}
        </div>
      </div>
    );
  }

  return (
    <div ref={containerRef} className={`h-[59vh] w-full}`}>
      <AutoSizer>
        {({ height, width }) => (
          <List
            ref={listRef}
            height={height}
            width={width}
            itemCount={items.length}
            itemSize={getItemSize}
            overscanCount={5}
            className="scrollbar-thin scrollbar-thumb-gray-300 scrollbar-track-gray-100"
          >
            {Row}
          </List>
        )}
      </AutoSizer>
    </div>);
}, (prevProps, nextProps) => {
  // Check if non-array props are the same
  const propsEqual = (
    prevProps.showActionCombobox === nextProps.showActionCombobox &&
    prevProps.isUpdating === nextProps.isUpdating &&
    prevProps.showSearchButton === nextProps.showSearchButton &&
    prevProps.showSkipButton === nextProps.showSkipButton &&
    prevProps.showAddButton === nextProps.showAddButton &&
    prevProps.onStatusChange === nextProps.onStatusChange &&
    prevProps.onActionChange === nextProps.onActionChange &&
    prevProps.onImportUpdate === nextProps.onImportUpdate &&
    prevProps.onButtonUpdate === nextProps.onButtonUpdate);
  if (!propsEqual) return false;

  // Deep comparison of items array - only re-render if contents actually changed
  if (prevProps.items.length !== nextProps.items.length) return false;
  for (let i = 0; i < prevProps.items.length; i++) {
    const prev = prevProps.items[i];
    const next = nextProps.items[i];

    // Safety check for undefined items
    if (!prev || !next) return false;

    // Compare essential properties that affect rendering
    if (
      prev.path !== next.path ||
      prev.status !== next.status ||
      prev.action !== next.action ||
      prev.title !== next.title ||
      (prev.series || []).length !== (next.series || []).length
    ) {
      return false;
    }
    // Compare series array if both have series
    // Exclude switch properties (useCover, isStorage, useTitle) from comparison
    // to prevent VirtualizedImportList re-render when switch is toggled
    if (prev.series && next.series) {
      for (let j = 0; j < prev.series.length; j++) {
        const prevSeries = prev.series[j];
        const nextSeries = next.series[j];

        // Safety check for undefined series
        if (!prevSeries || !nextSeries) return false;

        if (
          prevSeries.id !== nextSeries.id ||
          prevSeries.title !== nextSeries.title ||
          prevSeries.provider !== nextSeries.provider
        ) {
          return false;
        }
      }
    }
  } return true;
});

// Custom hook to detect if scrollbar is visible - optimized to reduce reflows
function useScrollbarDetection() {
  const [hasScrollbar, setHasScrollbar] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const timeoutRef = useRef<NodeJS.Timeout>(null);

  useEffect(() => {
    const checkScrollbar = () => {
      if (containerRef.current) {
        const { scrollHeight, clientHeight } = containerRef.current;
        const newHasScrollbar = scrollHeight > clientHeight;
        // Only update state if it actually changed to prevent unnecessary re-renders
        setHasScrollbar(prev => prev !== newHasScrollbar ? newHasScrollbar : prev);
      }
    };

    // Debounce the scrollbar check to prevent excessive calculations
    const debouncedCheck = () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
      timeoutRef.current = setTimeout(checkScrollbar, 100);
    };

    // Initial check
    checkScrollbar();

    // Use ResizeObserver with throttling to detect content changes
    const observer = new ResizeObserver(debouncedCheck);
    if (containerRef.current) {
      observer.observe(containerRef.current);
    }

    return () => {
      observer.disconnect();
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
    };
  }, []);

  return { hasScrollbar, containerRef };
}

// Scrollable tab content component with conditional padding - kept for non-virtualized content
function ScrollableTabContent({ children }: { children: React.ReactNode }) {
  const { hasScrollbar, containerRef } = useScrollbarDetection();

  return (
    <div
      ref={containerRef}
      className={`h-[59vh] w-full overflow-y-auto ${hasScrollbar ? 'pr-2' : ''}`}
    >
      {children}
    </div>
  );
}

interface ConfirmImportsStepProps {
  setError: (error: string | null) => void;
  setIsLoading: (loading: boolean) => void;
  setCanProgress: (canProgress: boolean) => void;
}

// Simple local state management - no backend sync
const useSimpleImportState = () => {
  const [imports, setImports] = useState<ImportInfo[]>([]);

  // Simple update function that just modifies local state
  const updateImport = useCallback((path: string, updates: Partial<ImportInfo>) => {
    setImports(prev =>
      prev.map(item =>
        item.path === path ? { ...item, ...updates } : item
      )
    );
  }, []);

  const updateStatus = useCallback((path: string, status: ImportStatus) => {
    updateImport(path, { status });
  }, [updateImport]);

  const updateAction = useCallback((path: string, action: Action) => {
    const status = action === Action.Add ? ImportStatus.Import : ImportStatus.Skip;
    updateImport(path, { action, status });
  }, [updateImport]);

  const updateChapter = useCallback((path: string, continueAfterChapter: number) => {
    updateImport(path, { continueAfterChapter });
  }, [updateImport]);

  const updateProviderToggle = useCallback((path: string, seriesIndex: number) => {
    setImports(prev =>
      prev.map(item => {
        if (item.path !== path || !item.series) return item;
        return {
          ...item,
          series: item.series.map((series, index) =>
            index === seriesIndex ? { ...series, preferred: !series.preferred } : series
          )
        };
      })
    );
  }, []);

  // Prevent propagation for switch changes (do nothing)
  const updateSeriesProperty = useCallback((path: string, seriesIndex: number, property: 'useCover' | 'isStorage' | 'useTitle', value: boolean) => {
    // No-op: do not update parent state for switch changes
  }, []);

  const replaceImport = useCallback((updatedImport: ImportInfo) => {
    setImports(prev =>
      prev.map(item =>
        item.path === updatedImport.path ? updatedImport : item
      )
    );
  }, []);

  return {
    imports,
    setImports,
    updateStatus,
    updateAction,
    updateChapter,
    updateProviderToggle,
    updateSeriesProperty,
    replaceImport
  };
};

interface ImportCardProps {
  import: ImportInfo;
  onStatusChange: (path: string, status: ImportStatus) => void;
  onActionChange: (path: string, action: Action) => void;
  onProviderToggle: (path: string, seriesIndex: number) => void;
  onChapterChange: (path: string, chapter: number) => void;
  onSeriesPropertyChange: (path: string, seriesIndex: number, property: 'useCover' | 'isStorage' | 'useTitle', value: boolean) => void;
  showActionCombobox?: boolean;
  isUpdating?: boolean;
  showSearchButton?: boolean;
  showSkipButton?: boolean;
  showAddButton?: boolean;
}

const ImportCard = React.memo(function ImportCard({ import: importItem, isUpdating, showActionCombobox, showSearchButton, showSkipButton, showAddButton }: ImportCardProps) {
  const importsCtx = useContext(ImportsContext);
  if (!importsCtx) throw new Error('ImportCard must be used within ImportsContext.Provider');

  // Use the import directly - no local state needed for most fields
  const preferredSeries = importItem.series?.find((series: SmallSeries) => series.preferred);
  const thumbnailSrc = preferredSeries?.thumbnailUrl;

  // --- Add local state for image src with fallback ---
  const [imgSrc, setImgSrc] = React.useState<string>(thumbnailSrc || '/kaizoku.net.png');
  React.useEffect(() => {
    setImgSrc(thumbnailSrc || '/kaizoku.net.png');
  }, [thumbnailSrc]);
  const handleImgError = React.useCallback(() => {
    setImgSrc('/kaizoku.net.png');
  }, []);

  // Local state for continueAfterChapter (like switches in SeriesCard)
  const [localChapter, setLocalChapter] = React.useState(importItem.continueAfterChapter ?? 0);
  React.useEffect(() => {
    setLocalChapter(importItem.continueAfterChapter ?? 0);
  }, [importItem.continueAfterChapter]);

  // Memoize the action value to prevent recalculation on every render
  const actionValue = useMemo(() => (importItem.action ?? Action.Add).toString(), [importItem.action]);

  // Event handlers that update the state directly
  const handleActionChange = React.useCallback((value: string) => {
    const newAction = parseInt(value) as Action;
    if (newAction !== importItem.action) {
      const newStatus = newAction === Action.Add ? ImportStatus.Import : ImportStatus.Skip;
      importsCtx.updateImportField(importItem.path, 'status', newStatus);
      importsCtx.updateImportField(importItem.path, 'action', newAction);
    }
  }, [importItem.action, importItem.path, importsCtx]);

  // Update both local and propagate to global state via context
  const handleChapterChange = React.useCallback((e: React.ChangeEvent<HTMLInputElement>) => {
    const chapter = parseInt(e.target.value) || 0;
    setLocalChapter(chapter);
    importsCtx.updateImportField(importItem.path, 'continueAfterChapter', chapter);
  }, [importItem.path, importsCtx]);

  // Search requester state
  const [searchRequesterOpen, setSearchRequesterOpen] = React.useState(false);

  const handleSearchClick = React.useCallback(() => {
    setSearchRequesterOpen(true);
  }, []);

  // Replace handleSearchResult with direct context update
  const handleSearchResult = React.useCallback((updatedImportInfo: ImportInfo) => {
    // Update the entire import object in global state
    importsCtx.updateImportField(updatedImportInfo.path, '', updatedImportInfo);
  }, [importsCtx]);
  const handleSkipClick = React.useCallback(() => {
    const newStatus = ImportStatus.Skip;
    const newAction = Action.Skip;
    importsCtx.updateImportField(importItem.path, 'status', newStatus);
    importsCtx.updateImportField(importItem.path, 'action', newAction);
  }, [importItem.path, importsCtx]);

  const handleAddClick = React.useCallback(() => {
    const newStatus = ImportStatus.Import;
    const newAction = Action.Add;
    importsCtx.updateImportField(importItem.path, 'status', newStatus);
    importsCtx.updateImportField(importItem.path, 'action', newAction);
  }, [importItem.path, importsCtx]);
  // Prevent event bubbling and unnecessary processing on pointer events
  const handleSelectPointerDown = React.useCallback((e: React.PointerEvent) => {
    e.stopPropagation();
  }, []);

  const handleSelectClick = React.useCallback((e: React.MouseEvent) => {
    e.stopPropagation();
  }, []);

  // Memoize the Select component to prevent unnecessary re-renders
  const actionSelect = useMemo(() => (
    <Select
      value={actionValue}
      onValueChange={handleActionChange}
      disabled={isUpdating}
    >
      <SelectTrigger
        className="w-40 will-change-auto transform-gpu"
        onPointerDown={handleSelectPointerDown}
        onClick={handleSelectClick}
      >
        <SelectValue placeholder="Select action" />
      </SelectTrigger>
      <SelectContent className="will-change-auto transform-gpu">
        <SelectItem value={Action.Add.toString()}>Add</SelectItem>
        <SelectItem value={Action.Skip.toString()}>Skip</SelectItem>
      </SelectContent>
    </Select>
  ), [actionValue, isUpdating, handleActionChange, handleSelectPointerDown, handleSelectClick]);

  return (
    <div >
      <Card className="w-full">
        <div className="flex">        {/* Thumbnail - Fixed poster aspect ratio */}
          {thumbnailSrc && (
            <div className="flex-shrink-0 p-3 pr-0">
              <TooltipProvider>
                <Tooltip>
                  <TooltipTrigger asChild>
                    <div className="cursor-pointer">
                      <Image
                        src={imgSrc}
                        alt={importItem.title || 'Series thumbnail'}
                        width={128}
                        height={196}
                        className="w-32 h-49 object-cover rounded-lg"
                        unoptimized
                        onError={handleImgError}
                      />
                    </div>
                  </TooltipTrigger>
                  <TooltipContent side="right" className="p-2 border-none bg-transparent shadow-2xl">
                    <Image
                      src={imgSrc}
                      alt={`${importItem.title || 'Series thumbnail'} - enlarged`}
                      width={288}
                      height={432}
                      className="w-72 h-108 object-cover rounded-lg shadow-lg z-50"
                      unoptimized
                      onError={handleImgError}
                    />
                  </TooltipContent>
                </Tooltip>
              </TooltipProvider>
            </div>
          )}

          {/* Card Content */}
          <div className="flex-1 flex flex-col">
            <CardHeader className="pb-3">
              <div className="flex items-start justify-between">              <div className="space-y-1 flex-1 min-w-0">
                <CardTitle className="text-base line-clamp-2">
                  {importItem.title || 'Unknown Title'}
                </CardTitle>
                <div className="text-sm text-muted-foreground">
                  Path: {importItem.path}
                </div>
              </div><div className="ml-4 flex items-center gap-4">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-medium">Continue After Chapter:</span>                  <Input
                      type="number"
                      min="0"
                      value={localChapter}
                      onChange={handleChapterChange}
                      className="w-24"
                      placeholder="0"
                    />
                  </div>                    {/* Show Search button when enabled */}
                  {(showSearchButton || !importItem.series || importItem.series.length===0) && (
                    <Button
                      size="sm"
                      onClick={handleSearchClick}
                      disabled={isUpdating}
                      className="flex items-center gap-2"
                    >
                      <Search className="h-4 w-4" />
                      Search
                    </Button>
                  )}                {/* Show Skip button when enabled */}
                  {showSkipButton && (
                    <Button
                      size="sm"
                      onClick={handleSkipClick}
                      disabled={isUpdating}
                      className="flex items-center gap-2"
                    >
                      <X className="h-4 w-4" />
                      Mismatch
                    </Button>
                  )}                {/* Show Add button when enabled and there is at least one series */}
                  {showAddButton  && (
                    <Button
                      size="sm"
                      onClick={handleAddClick}
                      disabled={isUpdating}
                      className="flex items-center gap-2"
                    >
                      <Plus className="h-4 w-4" />
                      Add
                    </Button>
                  )}

                  {/* Conditionally show Action combobox */}
                  {showActionCombobox && (
                    <div className="flex items-center gap-2 relative isolate">
                      <span className="text-sm font-medium">Action:</span>
                      <div className="relative z-10 contain-layout">
                        {actionSelect}
                      </div>
                    </div>
                  )}
                </div>
              </div>
            </CardHeader>
            <CardContent className="pt-0">
              <div className="space-y-3">              {/* Available Providers */}
                {importItem.series && importItem.series.length > 0 && (
                  <div>
                    <div className="flex items-center justify-between mb-2">
                    </div>
                    <div className="space-y-2">
                      {/* Grid layout for provider cards */}
                      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3">                      {importItem.series.map((series: SmallSeries, index: number) => (
                        <SeriesCard
                          key={`series-${series.id}-${series.provider}-${series.scanlator ?? ""}`}
                          series={series}
                          seriesIndex={index}
                          importPath={importItem.path}
                          onProviderToggle={(path: string, idx: number) => importsCtx.updateImportField(path, 'preferred', !series.preferred, idx)}
                          onSeriesPropertyChange={(path: string, idx: number, property: 'useCover' | 'isStorage' | 'useTitle', value: boolean) => importsCtx.updateImportField(path, property, value, idx)}
                        />
                      ))}
                      </div>
                    </div>
                  </div>
                )}
              </div>
            </CardContent>        </div>
        </div>
        {/* Search Series Requester */}      <SearchSeriesRequester
          open={searchRequesterOpen}
          onOpenChange={setSearchRequesterOpen} importTitle={importItem.title || 'Unknown Title'}
          importPath={importItem.path}
          onResult={handleSearchResult}
        />
      </Card><div className="pt-2"></div></div>
  );
}, (prevProps, nextProps) => {
  // Memoize ImportCard to prevent re-renders when other imports change
  // Exclude action from comparison since it's handled by memoized Select component
  const basicPropsEqual = (
    prevProps.import.path === nextProps.import.path &&
    prevProps.import.status === nextProps.import.status &&
    prevProps.showActionCombobox === nextProps.showActionCombobox &&
    prevProps.showSearchButton === nextProps.showSearchButton &&
    prevProps.showSkipButton === nextProps.showSkipButton &&
    prevProps.showAddButton === nextProps.showAddButton &&
    prevProps.isUpdating === nextProps.isUpdating
  );

  if (!basicPropsEqual) return false;

  // Compare series arrays without JSON.stringify for better performance
  const prevSeries = prevProps.import.series || [];
  const nextSeries = nextProps.import.series || [];

  if (prevSeries.length !== nextSeries.length) return false;

  for (let i = 0; i < prevSeries.length; i++) {
    const prev = prevSeries[i];
    const next = nextSeries[i];
    if (
      !prev || !next ||
      prev.id !== next.id ||
      prev.preferred !== next.preferred ||
      prev.title !== next.title ||
      prev.provider !== next.provider
    ) {
      return false;
    }
  }

  return true;
});

// --- Types for SeriesCard ---
interface SeriesCardProps {
  series: SmallSeries;
  seriesIndex: number;
  importPath: string;
  onProviderToggle: (path: string, idx: number) => void;
  onSeriesPropertyChange: (path: string, idx: number, property: 'useCover' | 'isStorage' | 'useTitle', value: boolean) => void;
}

// Series Card Component - now uses local state for switches
const SeriesCard = React.memo((props: SeriesCardProps) => {
  const { series, seriesIndex, importPath, onProviderToggle, onSeriesPropertyChange } = props;
  const importsCtx = useContext(ImportsContext);
  if (!importsCtx) throw new Error('SeriesCard must be used within ImportsContext.Provider');

  // Local state for switches and preferred
  const [isStorage, setIsStorage] = React.useState(series.isStorage);
  const [useCover, setUseCover] = React.useState(series.useCover);
  const [useTitle, setUseTitle] = React.useState(series.useTitle);
  const [preferred, setPreferred] = React.useState(series.preferred);

  // Keep local state in sync if parent changes (e.g. on import switch)
  React.useEffect(() => { setIsStorage(series.isStorage); }, [series.isStorage]);
  React.useEffect(() => { setUseCover(series.useCover); }, [series.useCover]);
  React.useEffect(() => { setUseTitle(series.useTitle); }, [series.useTitle]);
  React.useEffect(() => { setPreferred(series.preferred); }, [series.preferred]);

  // Switch handlers: update local state and propagate to global state via context
  const handleStorageChange = React.useCallback((checked: boolean) => {
    setIsStorage(checked);
    importsCtx.updateImportField(importPath, 'isStorage', checked, seriesIndex);
  }, [importPath, seriesIndex, importsCtx]);
  const handleCoverChange = React.useCallback((checked: boolean) => {
    setUseCover(checked);
    importsCtx.updateImportField(importPath, 'useCover', checked, seriesIndex);
  }, [importPath, seriesIndex, importsCtx]);
  const handleTitleChange = React.useCallback((checked: boolean) => {
    setUseTitle(checked);
    importsCtx.updateImportField(importPath, 'useTitle', checked, seriesIndex);
  }, [importPath, seriesIndex, importsCtx]);
  // Provider toggle: update local preferred and propagate
  const handleProviderClick = React.useCallback(() => {
    setPreferred((prev: boolean) => !prev);
    onProviderToggle(importPath, seriesIndex);
  }, [importPath, seriesIndex, onProviderToggle]);

  return (
    <div
      className={`flex flex-col gap-2 pr-3 pl-3 pt-2 pb-2 rounded-lg border transition-all duration-200 ${preferred
        ? 'bg-primary text-primary-foreground shadow-md border-primary'
        : 'bg-card hover:bg-muted hover:border-primary/50'
        }`}
    >
      {/* Main series info - clickable for preference toggle */}
      <div
        className="flex flex-col items-start text-left flex-0 min-w-0 cursor-pointer"
        onClick={handleProviderClick}
      >
        <div className="flex items-center gap-1 truncate w-full">
          <span className="font-medium text-sm truncate flex-1 min-w-0" title={series.title}>
            {series.title}
          </span>
          <ReactCountryFlag
            countryCode={getCountryCodeForLanguage(series.lang)}
            svg
            style={{
              width: "16px",
              height: "12px",
              flexShrink: 0,
            }}
            title={series.lang.toLowerCase()}
          />
        </div>
        <span className={`text-xs ${preferred ? 'opacity-90' : 'opacity-70'} truncate w-full`}>
          {series.url ? (
            <a
              href={series.url}
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-1 hover:underline"
              onClick={(e) => e.stopPropagation()}
            >
              {series.provider}
              <ExternalLink className="h-3 w-3" />
            </a>
          ) : (
            series.provider
          )}
          {series.scanlator && series.scanlator !== series.provider && ` • ${series.scanlator}`}
          &nbsp;• {series.chapterCount} chapters
          {series.lastChapter && ` • Last: ${series.lastChapter}`}
        </span>
      </div>

      {/* Switch controls */}
      <div className="flex items-center justify-between pt-1 border-t border-border/20 gap-1">
        <div
          className="flex items-center gap-1"
          onClick={(e) => e.stopPropagation()}
        >
          <Tooltip>
            <TooltipTrigger asChild>
              <span className={`text-xs ${preferred ? 'text-primary-foreground/90' : 'text-muted-foreground'}`}>
                Permanent
              </span>
            </TooltipTrigger>
            <TooltipContent>
              <p><b>Permanent sources</b> always download new chapters and replace any existing copies from non-permanent sources.<br /><b>Non-permanent sources</b> only download a chapter if they are the first to have it available.</p>
            </TooltipContent>
          </Tooltip>
          <Switch
            checked={isStorage}
            onCheckedChange={handleStorageChange}
            className="scale-75"
          />
        </div>
        <div
          className="flex items-center gap-1"
          onClick={(e) => e.stopPropagation()}
        >
          <span className={`text-xs ${preferred ? 'text-primary-foreground/90' : 'text-muted-foreground'}`}>
            Cover
          </span>
          <Switch
            checked={useCover}
            onCheckedChange={handleCoverChange}
            className="scale-75"
          />
        </div>
        <div
          className="flex items-center gap-1"
          onClick={(e) => e.stopPropagation()}
        >
          <span className={`text-xs ${preferred ? 'text-primary-foreground/90' : 'text-muted-foreground'}`}>
            Title
          </span>
          <Switch
            checked={useTitle}
            onCheckedChange={handleTitleChange}
            className="scale-75"
          />
        </div>
      </div>
    </div>
  );
}, (prevProps: SeriesCardProps, nextProps: SeriesCardProps) => {
  // Compare essential properties that affect rendering
  const seriesEqual =
    prevProps.series.id === nextProps.series.id &&
    prevProps.series.title === nextProps.series.title &&
    prevProps.series.provider === nextProps.series.provider &&
    prevProps.series.chapterCount === nextProps.series.chapterCount;
  // switches and preferred are now local state, so do not compare them

  const propsEqual =
    prevProps.seriesIndex === nextProps.seriesIndex &&
    prevProps.importPath === nextProps.importPath &&
    seriesEqual;

  return propsEqual;
});

SeriesCard.displayName = 'SeriesCard';

// --- Dual State Model for Imports ---
// Accepts an external callback for all UI changes
function useDualImportState(globalImports: ImportInfo[], onAnyImportChange: (updated: ImportInfo[]) => void) {
  const [localImports, setLocalImports] = useState<ImportInfo[]>([]);
  const prevGlobalRef = useRef<ImportInfo[]>([]);

  // Helper to compare only action, status, and SmallSeries collection
  function shouldSyncLocal(global: ImportInfo[], prevGlobal: ImportInfo[]): boolean {
    if (global.length !== prevGlobal.length) return true;
    for (let i = 0; i < global.length; i++) {
      const g = global[i];
      const p = prevGlobal[i];
      if (!g || !p) return true;
      if (g.action !== p.action || g.status !== p.status) return true;
      const gSeries = g.series || [];
      const pSeries = p.series || [];
      if (gSeries.length !== pSeries.length) return true;
      for (let j = 0; j < gSeries.length; j++) {
        if (!gSeries[j] || !pSeries[j]) return true;
        if (gSeries[j].id !== pSeries[j].id) return true;
      }
    }
    return false;
  }

  // Sync local state from global only if action, status, or SmallSeries collection changes
  useEffect(() => {
    const r = shouldSyncLocal(globalImports, prevGlobalRef.current);
    if (r) {
      setLocalImports(globalImports.map(i => ({ ...i, series: i.series ? i.series.map(s => ({ ...s })) : [] })));
      prevGlobalRef.current = globalImports;
    }
  }, [globalImports]);

  // All update helpers: update local, then call external callback
  const updateImport = useCallback((path: string, updates: Partial<ImportInfo>) => {
    setLocalImports(prev => {
      const updated = prev.map(item => item.path === path ? { ...item, ...updates } : item);
      onAnyImportChange(updated);
      return updated;
    });
  }, [onAnyImportChange]);

  const updateStatus = useCallback((path: string, status: ImportStatus) => {
    updateImport(path, { status });
  }, [updateImport]);

  const updateAction = useCallback((path: string, action: Action) => {
    const status = action === Action.Add ? ImportStatus.Import : ImportStatus.Skip;
    updateImport(path, { action, status });
  }, [updateImport]);

  const updateChapter = useCallback((path: string, continueAfterChapter: number) => {
    updateImport(path, { continueAfterChapter });
  }, [updateImport]);

  const updateProviderToggle = useCallback((path: string, seriesIndex: number) => {
    setLocalImports(prev => {
      const updated = prev.map(item => {
        if (item.path !== path || !item.series) return item;
        return {
          ...item,
          series: item.series.map((series, idx) => idx === seriesIndex ? { ...series, preferred: !series.preferred } : series)
        };
      });
      onAnyImportChange(updated);
      return updated;
    });
  }, [onAnyImportChange]);

  // Switches: update local and propagate to global/backend
  const updateSeriesProperty = useCallback((path: string, seriesIndex: number, property: 'useCover' | 'isStorage' | 'useTitle', value: boolean) => {
    setLocalImports(prev => {
      const updated = prev.map(item => {
        if (item.path !== path || !item.series) return item;
        return {
          ...item,
          series: item.series.map((series, idx) => idx === seriesIndex ? { ...series, [property]: value } : series)
        };
      });
      onAnyImportChange(updated);
      return updated;
    });
  }, [onAnyImportChange]);

  const replaceImport = useCallback((updatedImport: ImportInfo) => {
    setLocalImports(prev => {
      const updated = prev.map(item => item.path === updatedImport.path ? updatedImport : item);
      onAnyImportChange(updated);
      return updated;
    });
  }, [onAnyImportChange]);

  return {
    localImports,
    setLocalImports,
    updateStatus,
    updateAction,
    updateChapter,
    updateProviderToggle,
    updateSeriesProperty,
    replaceImport
  };
}

export function ConfirmImportsStep({ setError, setIsLoading, setCanProgress }: ConfirmImportsStepProps) {
  const [isUpdating, setIsUpdating] = useState(false);
  const [activeTab, setActiveTab] = useState<string>("import");
  const { data: importsData, isLoading: importsLoading, refetch } = useSetupWizardImports();
  const importMutation = useSetupWizardImportSeries();
  const updateMutation = useSetupWizardUpdateImport();

  // --- Dual state: global (API) and local (UI) ---
  const [globalImports, setGlobalImports] = useState<ImportInfo[]>([]);
  useEffect(() => {
    if (importsData) setGlobalImports(importsData);
  }, [importsData]);

  // Use dual state hook to get localImports
  const {
    localImports,
    setLocalImports,
    updateStatus: handleStatusChange,
    updateAction: handleActionChange,
    updateProviderToggle: handleProviderToggle,
    updateChapter: handleChapterChange,
    updateSeriesProperty: handleSeriesPropertyChange,
    replaceImport: handleReplaceImport
  } = useDualImportState(globalImports, setGlobalImports);

  // Debounce map for per-ImportInfo backend update
  const debounceTimeoutsRef = useRef<{ [path: string]: NodeJS.Timeout | number }>({});

  // Context update function: called by UI components directly
  const updateImportField = useCallback((path: string, field: string, value: any, seriesIndex?: number) => {
    setGlobalImports(prev => {
      // Find the import to update from the latest state
      const importToUpdate = prev.find(item => item.path === path);
      if (!importToUpdate) return prev;
      let updatedImport: ImportInfo;
      if (seriesIndex !== undefined && importToUpdate.series) {
        // Update a field in a specific series
        updatedImport = {
          ...importToUpdate,
          series: importToUpdate.series.map((series, idx) =>
            idx === seriesIndex ? { ...series, [field]: value } : series
          )
        };
      } else if (field === '' && typeof value === 'object') {
        // Replace the entire import object (for search result)
        updatedImport = { ...value };
      } else {
        // Update a field in the import itself
        updatedImport = { ...importToUpdate, [field]: value };
      }
      // Optimistically update globalImports
      const newImports = prev.map(item => item.path === path ? updatedImport : item);

      // Debounce backend update per ImportInfo (by path)
      if (debounceTimeoutsRef.current[path]) {
        clearTimeout(debounceTimeoutsRef.current[path]);
      }
      debounceTimeoutsRef.current[path] = setTimeout(() => {
        updateMutation.mutate(updatedImport, {
          onError: (error) => {
            setError('Failed to update import. Please try again.');
            refetch();
          }
        });
        delete debounceTimeoutsRef.current[path];
      }, 5000);

      return newImports;
    });
  }, [updateMutation, setError, refetch]);

  // Fetch imports on mount
  useEffect(() => {
    refetch().catch((error) => {
      console.error('Failed to fetch imports:', error);
      setError('Failed to load imports. Please try again.');
    });
  }, [refetch, setError]);

  // Update loading and progress states
  useEffect(() => {
    setIsLoading(importsLoading || importMutation.isPending);
    const validImports = globalImports.filter((item: ImportInfo) => {
      return true;
      //if (item.status !== ImportStatus.Import) return true;
      //return item.series?.some((series: SmallSeries) => series.preferred) || false;
    });
    setCanProgress(globalImports.length > 0 && !importsLoading && !importMutation.isPending && validImports.length === globalImports.length);
  }, [importsLoading, importMutation.isPending, globalImports, setIsLoading, setCanProgress]);

  // Filtered arrays
  const importsToProcess = useMemo(() => {
    return globalImports.filter((item: ImportInfo) => item.status === ImportStatus.Import);
  }, [globalImports]);

  const skippedImports = useMemo(() => {
    return globalImports.filter((item: ImportInfo) => item.status === ImportStatus.Skip);
  }, [globalImports]);

  const unchangedImports = useMemo(() => {
    return globalImports.filter((item: ImportInfo) => item.status === ImportStatus.DoNotChange);
  }, [globalImports]);

  const completedImports = useMemo(() => {
    return globalImports.filter((item: ImportInfo) => item.status === ImportStatus.Completed);
  }, [globalImports]);

  if (importsLoading) {
    return (
      <div className="flex items-center justify-center min-h-[200px]">
        <div className="text-muted-foreground">Loading imports...</div>
      </div>
    );
  }

  if (globalImports.length === 0) {
    return (
      <div className="text-center space-y-4">
        <div className="text-muted-foreground">
          Loading Series
        </div>
      </div>
    );
  }

  return (
    <ImportsContext.Provider value={{ updateImportField }}>
      <div className="space-y-4">
        <div className="space-y-2">
          <div className="text-sm text-muted-foreground">
            Review the imported series below. Only items marked as <b>Not Matched or Mismatched</b> will not be imported or updated.<br/>
            You can revise items by marking them as mismatched in the <b>Add/Finished</b> tab, and search for correct matches in the <b>Mismatched</b> tab.
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab} className="w-full">
          <div className="flex items-center justify-between mb-2">
            <TabsList className="grid w-auto grid-cols-4">
              <TabsTrigger value="import" className="flex items-center gap-2">
                <span className="w-2 h-2 bg-green-500 rounded-full"></span>
                Add ({importsToProcess.length})
              </TabsTrigger>
              <TabsTrigger value="completed" className="flex items-center gap-2">
                <span className="w-2 h-2 bg-violet-500 rounded-full"></span>
                Finished ({completedImports.length})
              </TabsTrigger>
              <TabsTrigger value="unchanged" className="flex items-center gap-2">
                <span className="w-2 h-2 bg-blue-500 rounded-full"></span>
                Already Imported ({unchangedImports.length})
              </TabsTrigger>
              <TabsTrigger value="skip" className="flex items-center gap-2">
                <span className="w-2 h-2 bg-gray-500 rounded-full"></span>
                Not Matched or Mismatched ({skippedImports.length})
              </TabsTrigger>
            </TabsList>
          </div>

          {activeTab === "import" && (
            <TabsContent value="import" className="space-y-4" forceMount={true}>
              <VirtualizedImportList
                items={importsToProcess}
                onStatusChange={handleStatusChange}
                onActionChange={handleActionChange}
                onProviderToggle={handleProviderToggle}
                onChapterChange={handleChapterChange}
                onSeriesPropertyChange={handleSeriesPropertyChange}
                showActionCombobox={false}
                showSkipButton={true}
                isUpdating={isUpdating}
              />
            </TabsContent>
          )}
          {activeTab === "unchanged" && (
            <TabsContent value="unchanged" className="space-y-4" forceMount={true}>
              <VirtualizedImportList
                items={unchangedImports}
                onStatusChange={handleStatusChange}
                onActionChange={handleActionChange}
                onProviderToggle={handleProviderToggle}
                onChapterChange={handleChapterChange}
                onSeriesPropertyChange={handleSeriesPropertyChange}
                showActionCombobox={false}
                isUpdating={isUpdating}
              />
            </TabsContent>
          )}
          {activeTab === "completed" && (
            <TabsContent value="completed" className="space-y-4" forceMount={true}>
              <VirtualizedImportList
                items={completedImports}
                onStatusChange={handleStatusChange}
                onActionChange={handleActionChange}
                onProviderToggle={handleProviderToggle}
                onChapterChange={handleChapterChange}
                showSkipButton={true}
                onSeriesPropertyChange={handleSeriesPropertyChange}
                showActionCombobox={false}
                isUpdating={isUpdating}
              />
            </TabsContent>
          )}
          {activeTab === "skip" && (
            <TabsContent value="skip" className="space-y-4" forceMount={true}>
              <VirtualizedImportList
                items={skippedImports}
                onStatusChange={handleStatusChange}
                onActionChange={handleActionChange}
                onProviderToggle={handleProviderToggle}
                onChapterChange={handleChapterChange}
                onSeriesPropertyChange={handleSeriesPropertyChange}
                showActionCombobox={false}
                showSearchButton={true}
                showAddButton={true}
                isUpdating={isUpdating}
              />
            </TabsContent>
          )}
        </Tabs>
      </div>
    </ImportsContext.Provider>
  );
}
