"use client";

import { type AddSeriesState } from "@/components/comp/series/add-series";
import { AlertTriangle, Loader2, Search } from "lucide-react";
import { type LinkedSeries, type ExistingSource } from "@/lib/api/types";
import { useSearchSeries, useAvailableSearchSources } from "@/lib/api/hooks/useSearch";
import { useSettings } from "@/lib/api/hooks/useSettings";
import { searchService } from "@/lib/api/services/searchService";
import { getProgressHub } from "@/lib/api/signalr/progressHub";
import React from "react";
import { useDebounce } from "use-debounce";
import Image from "next/image";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { usePermission } from "@/hooks/use-permission";
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";
import { MultiSelectSources } from "@/components/ui/multi-select-sources";

// ---------------------------------------------------------------------------
// Component
// ---------------------------------------------------------------------------
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
  const canBrowseSources = usePermission('canBrowseSources');

  const [searchValue, setSearchValue] = React.useState(formState.searchKeyword || "");
  const [debouncedSearchValue] = useDebounce(searchValue, 800);

  // Only fetch available search sources if user has permission to browse/select them
  const { data: allAvailableSources = [] } = useAvailableSearchSources();

  const availableSources = allAvailableSources;

  // State for selected search sources
  const [selectedSources, setSelectedSources] = React.useState<string[]>([]);
  // Debounce the selected sources to prevent too frequent searches when changing sources
  const [debouncedSelectedSources] = useDebounce(selectedSources, 3000);

  // Key for localStorage - make it unique for different modes
  const LOCAL_STORAGE_KEY = existingSources && existingSources.length > 0
    ? 'rensaio.selectedSources.addSources'
    : 'rensaio.selectedSources.addSeries';

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

  // When user has CanBrowseSources: search with their selected sources
  // When user does NOT have CanBrowseSources: search all sources (don't pass searchSources, backend defaults to all)
  const searchSourcesParam = canBrowseSources
    ? (debouncedSelectedSources.length > 0 ? debouncedSelectedSources : undefined)
    : undefined;

  const isSearchReady = canBrowseSources
    ? debouncedSearchValue.length >= 3 && debouncedSelectedSources.length > 0
    : debouncedSearchValue.length >= 3;

  const { data: searchResults, isLoading, error, isFetching } = useSearchSeries(
    {
      keyword: debouncedSearchValue,
      searchSources: searchSourcesParam
    },
    { enabled: isSearchReady }
  );

  // ---------------------------------------------------------------------------
  // Automatic discovery search: whenever the normal search fires, a background
  // sweep over eligible NOT-installed sources starts too (same query/debounce).
  // Installed results render immediately as always; discovery results stream in
  // over SignalR and merge into the same relevance-ordered list, badged
  // "Not installed". Gated on the discoveryIncludeInSearch setting and on
  // add-series rights (selecting a discovery result installs its extension).
  // ---------------------------------------------------------------------------
  const canAddSeries = usePermission('canAddSeries');
  const { data: appSettings } = useSettings();
  const discoveryEnabled = canAddSeries && (appSettings?.discoveryIncludeInSearch ?? true);

  const [discoveryProgress, setDiscoveryProgress] = React.useState<{
    stage: string;
    completed: number;
    total: number;
    totalSources: number;
  } | null>(null);
  const discoverySearchIdRef = React.useRef<string | null>(null);

  /** Merge installed + discovery results into one relevance-ordered list. */
  const mergeSorted = React.useCallback((installed: LinkedSeries[], discovery: LinkedSeries[]): LinkedSeries[] => {
    return [...installed, ...discovery].sort(
      (a, b) => ((b.relevance ?? -1) - (a.relevance ?? -1)) || a.title.localeCompare(b.title)
    );
  }, []);

  /** Upsert streamed discovery results (deduped by mihonId) into the unified list. */
  const applyDiscoveryResults = React.useCallback((incoming: LinkedSeries[], replace: boolean) => {
    setFormState(prev => {
      const byId = new Map<string, LinkedSeries>();
      if (!replace) {
        prev.discoveryLinkedSeries.forEach(s => { if (s.mihonId) byId.set(s.mihonId, s); });
      }
      incoming.forEach(s => { if (s.mihonId) byId.set(s.mihonId, s); });
      const discovery = Array.from(byId.values());
      const installed = prev.allLinkedSeries.filter(s => s.installed !== false);
      return {
        ...prev,
        discoveryLinkedSeries: discovery,
        allLinkedSeries: mergeSorted(installed, discovery),
      };
    });
  }, [setFormState, mergeSorted]);

  // One effect owns the whole discovery lifecycle for the current query: it clears stale
  // results, starts (or attaches to) the sweep, subscribes to its hub events, and on
  // cleanup (query change / dialog close / leaving the step) cancels the in-flight sweep
  // so its worker processes are killed server-side.
  React.useEffect(() => {
    discoverySearchIdRef.current = null;
    setDiscoveryProgress(null);
    setFormState(prev => {
      if (prev.discoveryLinkedSeries.length === 0) return prev;
      return {
        ...prev,
        discoveryLinkedSeries: [],
        allLinkedSeries: prev.allLinkedSeries.filter(s => s.installed !== false),
      };
    });

    if (!discoveryEnabled || debouncedSearchValue.trim().length < 3) return;

    let disposed = false;
    // Final events seen for sweeps we may not have identified yet (completed before the
    // start call returned). Checked right after the searchId becomes known.
    const finalEvents = new Map<string, string>();

    const finishWithAuthoritativeResults = async () => {
      // The completed sweep is now cached server-side; re-fetching heals any events that
      // slipped between the attach snapshot and our subscription.
      try {
        const finalRes = await searchService.startDiscovery(debouncedSearchValue);
        if (!disposed && finalRes.done) {
          applyDiscoveryResults(finalRes.results, true);
        }
      } catch (err) {
        console.warn('[SearchSeriesStep] failed to fetch final discovery results:', err);
      } finally {
        if (!disposed) {
          discoverySearchIdRef.current = null;
          setDiscoveryProgress(null);
        }
      }
    };

    const unsubscribe = getProgressHub().onDiscovery((evt) => {
      if (disposed) return;
      const currentId = discoverySearchIdRef.current;
      if (!currentId) {
        if (evt.type === 'completed' || evt.type === 'cancelled' || evt.type === 'failed') {
          finalEvents.set(evt.searchId, evt.type);
        }
        return;
      }
      if (evt.searchId !== currentId) return;

      if (evt.type === 'results' && evt.results && evt.results.length > 0) {
        applyDiscoveryResults(evt.results, false);
      }
      if (evt.type === 'results' || evt.type === 'progress') {
        setDiscoveryProgress(prev => ({
          stage: evt.stage ?? prev?.stage ?? 'searching',
          completed: evt.stage && evt.stage !== prev?.stage
            ? evt.completedExtensions
            : Math.max(prev?.completed ?? 0, evt.completedExtensions),
          total: evt.totalExtensions > 0 ? evt.totalExtensions : (prev?.total ?? 0),
          totalSources: prev?.totalSources ?? 0,
        }));
      } else if (evt.type === 'completed') {
        void finishWithAuthoritativeResults();
      } else if (evt.type === 'cancelled' || evt.type === 'failed') {
        discoverySearchIdRef.current = null;
        setDiscoveryProgress(null);
      }
    });

    void (async () => {
      try {
        await getProgressHub().startConnection();
        const start = await searchService.startDiscovery(debouncedSearchValue);
        if (disposed) {
          if (start.searchId) {
            void searchService.cancelDiscovery(start.searchId).catch(() => undefined);
          }
          return;
        }
        if (start.results.length > 0) {
          applyDiscoveryResults(start.results, true);
        }
        if (start.done || !start.searchId) return; // cache hit, disabled, or nothing eligible
        discoverySearchIdRef.current = start.searchId;
        setDiscoveryProgress({
          stage: start.stage ?? 'preparing',
          completed: start.completedExtensions,
          total: start.totalExtensions,
          totalSources: start.totalSources,
        });
        // Sweep may have finished while the start call was in flight.
        const final = finalEvents.get(start.searchId);
        if (final === 'completed') {
          void finishWithAuthoritativeResults();
        } else if (final === 'cancelled' || final === 'failed') {
          discoverySearchIdRef.current = null;
          setDiscoveryProgress(null);
        }
      } catch (err) {
        console.warn('[SearchSeriesStep] discovery start failed:', err);
        if (!disposed) setDiscoveryProgress(null);
      }
    })();

    return () => {
      disposed = true;
      unsubscribe();
      if (discoverySearchIdRef.current) {
        void searchService.cancelDiscovery(discoverySearchIdRef.current).catch(() => undefined);
        discoverySearchIdRef.current = null;
      }
    };
  }, [debouncedSearchValue, discoveryEnabled, setFormState, applyDiscoveryResults]);

  React.useEffect(() => {
    if (searchResults) {
      setFormState(prev => {
        // Merge installed results with any streamed discovery results, relevance-ordered
        const merged = mergeSorted(searchResults, prev.discoveryLinkedSeries);
        // Validate existing selections against new search results
        const newSearchResultIds = merged.map(series => series.mihonId ?? series.providerId);
        const validatedSelections = prev.selectedLinkedSeries.filter(selectedId =>
          newSearchResultIds.includes(selectedId)
        );

        return {
          ...prev,
          allLinkedSeries: merged,
          searchKeyword: debouncedSearchValue,
          selectedLinkedSeries: validatedSelections,
        };
      });
    }
  }, [searchResults, debouncedSearchValue, setFormState, mergeSorted]);

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

  // Local-only focused row tracking — purely visual, not in AddSeriesState
  const [lastFocusedId, setLastFocusedId] = React.useState<string | null>(null);

  const isSearching = (isLoading || isFetching) && debouncedSearchValue.length >= 3;
  const hasQuery = searchValue.length > 0;
  const hasResults = allSeries.length > 0;
  const isDiscoveryRunning = discoveryProgress !== null;

  const renderSeriesRow = (series: LinkedSeries) => {
    const seriesId = getSeriesId(series);
    const isSelected = isSeriesSelected(seriesId);
    const isFocused = lastFocusedId === seriesId;
    const isDiscovery = series.installed === false;

    return (
      <div
        key={seriesId}
        className={`res-row${isSelected ? " selected" : ""}${isFocused ? " focused" : ""}`}
        onClick={() => {
          handleSeriesToggle(seriesId, !isSelected);
          setLastFocusedId(seriesId);
        }}
      >
        {/* Slot 1: accent bar */}
        <div
          className="accent"
          style={isSelected ? { background: "hsl(var(--primary))" } : undefined}
        />

        {/* Slot 2: cover thumbnail */}
        <div className="res-cv">
          <Image
            src={formatThumbnailUrl(series.thumbnailUrl)}
            alt={series.title}
            fill
            sizes="(max-width: 640px) 44px, 48px"
            className="object-cover"
          />
        </div>

        {/* Slot 3: body */}
        <div className="res-body">
          <div className="res-title">{series.title}</div>
          <div className="res-meta">
            <span className="src-badge">
              {series.provider}
            </span>
            <ReactCountryFlag
              countryCode={getCountryCodeForLanguage(series.lang)}
              svg
              style={{ width: 16, height: 12 }}
              title={`${series.lang.toUpperCase()} (${getCountryCodeForLanguage(series.lang)})`}
            />
            {isDiscovery && (
              <span
                className="font-mono"
                style={{
                  fontSize: 10,
                  padding: "1px 6px",
                  borderRadius: 4,
                  border: "1px solid hsl(38 92% 50% / 0.45)",
                  color: "hsl(38 92% 55%)",
                  whiteSpace: "nowrap",
                }}
                title={`Selecting this will install the ${series.extensionName ?? series.extensionPkg ?? ""} extension`}
              >
                Not installed
              </span>
            )}
          </div>
        </div>

        {/* Slot 4: selected indicator */}
        <div className="res-tail">
          {isSelected && (
            <span
              className="sel-added font-mono"
            >
              ✓ added
            </span>
          )}
        </div>
      </div>
    );
  };

  return (
    <div className="search-step">
      {/* Search input row — full width */}
      <div className="cmd-input-wrap">
          <Search className="icon" style={{ width: 22, height: 22 }} />
          <input
            className="cmd-input"
            type="search"
            placeholder="Search for a series…"
            autoFocus
            value={searchValue}
            onPointerDown={(e) => e.stopPropagation()}
            onChange={(e) => setSearchValue(e.target.value)}
          />
          <div className="cmd-spinner-slot" aria-hidden={!isSearching}>
            {isSearching && (
              <Loader2
                className="h-4 w-4 animate-spin"
                style={{ color: "hsl(var(--as-fg-muted))" }}
              />
            )}
          </div>
        </div>

        {/* Sources selector — its own row, right-aligned */}
        {canBrowseSources && availableSources.length > 0 && (
          <div
            className="cmd-sources-row"
            onPointerDown={(e) => e.stopPropagation()}
          >
            <div className="src-dropdown-slot">
              <MultiSelectSources
                sources={availableSources}
                selectedSources={selectedSources}
                onSelectionChange={setSelectedSources}
              />
            </div>
          </div>
        )}

        {/* Results area */}
        {error ? (
          <div
            className="flex items-center gap-2 px-5 py-3"
            style={{ color: "hsl(0 72% 51%)", fontSize: 12 }}
          >
            <AlertTriangle style={{ width: 14, height: 14, flexShrink: 0 }} />
            <span>{error.message}</span>
          </div>
        ) : !hasQuery ? (
          <div className="res-list">
            <p
              className="stage-label"
              style={{ justifyContent: "center", opacity: 0.45, fontSize: 13, padding: "24px 22px" }}
            >
              Start typing to search…
            </p>
          </div>
        ) : !hasResults && !isSearching && !isDiscoveryRunning ? (
          <div className="res-list">
            <p
              style={{
                color: "hsl(var(--as-fg-muted))",
                fontSize: 13,
                padding: "24px 22px",
                textAlign: "center",
              }}
            >
              {debouncedSearchValue.length < 3
                ? "Keep typing — search starts at 3 characters"
                : "No results found"}
            </p>
          </div>
        ) : (
          <div className="res-list" data-vaul-no-drag>
            {allSeries.map(renderSeriesRow)}
          </div>
        )}

        {/* Subtle streaming-discovery progress affordance; disappears when the sweep completes */}
        {!error && isDiscoveryRunning && discoveryProgress && (
          <p
            className="font-mono"
            style={{
              display: "flex",
              alignItems: "center",
              gap: 8,
              padding: "10px 22px",
              fontSize: 11,
              color: "hsl(var(--as-fg-muted))",
              opacity: 0.75,
              borderTop: "1px solid hsla(0 0% 100% / 0.06)",
            }}
          >
            <Loader2 className="h-3.5 w-3.5 animate-spin" style={{ flexShrink: 0 }} />
            <span>
              {discoveryProgress.stage === 'preparing'
                ? `Preparing ${discoveryProgress.totalSources} more sources…`
                : `Checking ${discoveryProgress.totalSources} more sources…`}
              {" "}
              {Math.min(discoveryProgress.completed, discoveryProgress.total)} of {discoveryProgress.total} extensions done
            </span>
          </p>
        )}
    </div>
  );
}
