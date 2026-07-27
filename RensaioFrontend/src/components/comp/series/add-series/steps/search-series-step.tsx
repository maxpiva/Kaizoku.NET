"use client";

import { type AddSeriesState } from "@/components/comp/series/add-series";
import { AlertTriangle, Globe, Loader2, Search } from "lucide-react";
import { type LinkedSeries, type ExistingSource } from "@/lib/api/types";
import { useSearchSeries, useAvailableSearchSources, useDiscoverySources, useDiscoverySearch } from "@/lib/api/hooks/useSearch";
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
  // Discovery search ("search more sources"): opt-in search across sources whose
  // extensions are NOT installed. Only offered to users who can add series, since
  // selecting a discovery result installs the extension.
  // ---------------------------------------------------------------------------
  const canAddSeries = usePermission('canAddSeries');
  const { data: discoveryInfo } = useDiscoverySources({ enabled: canAddSeries });
  const [discoveryRequested, setDiscoveryRequested] = React.useState(false);

  // A new search term resets the opt-in and clears stale discovery results.
  React.useEffect(() => {
    setDiscoveryRequested(false);
    setFormState(prev => {
      if (prev.discoveryLinkedSeries.length === 0) return prev;
      return {
        ...prev,
        discoveryLinkedSeries: [],
        allLinkedSeries: prev.allLinkedSeries.filter(s => s.installed !== false),
      };
    });
  }, [debouncedSearchValue, setFormState]);

  const {
    data: discoveryResults,
    isFetching: isDiscoverySearching,
    error: discoveryError,
  } = useDiscoverySearch(debouncedSearchValue, {
    enabled: canAddSeries && discoveryRequested && debouncedSearchValue.length >= 3,
  });

  React.useEffect(() => {
    if (discoveryResults) {
      setFormState(prev => ({
        ...prev,
        discoveryLinkedSeries: discoveryResults,
        allLinkedSeries: [...prev.allLinkedSeries.filter(s => s.installed !== false), ...discoveryResults],
      }));
    }
  }, [discoveryResults, setFormState]);

  React.useEffect(() => {
    if (searchResults) {
      setFormState(prev => {
        // Keep any discovery results appended after the regular results
        const merged = [...searchResults, ...prev.discoveryLinkedSeries];
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
  const installedSeries = allSeries.filter((s) => s.installed !== false);
  const discoverySeries = formState.discoveryLinkedSeries;

  // Local-only focused row tracking — purely visual, not in AddSeriesState
  const [lastFocusedId, setLastFocusedId] = React.useState<string | null>(null);

  const isSearching = (isLoading || isFetching) && debouncedSearchValue.length >= 3;
  const hasQuery = searchValue.length > 0;
  const hasResults = installedSeries.length > 0;
  const showDiscovery =
    canAddSeries &&
    !error &&
    debouncedSearchValue.length >= 3 &&
    (discoveryInfo?.sourceCount ?? 0) > 0;

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
        ) : !hasResults && !isSearching ? (
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
            {installedSeries.map(renderSeriesRow)}
          </div>
        )}

        {/* Discovery ("search more sources") section — sources from extensions not installed */}
        {showDiscovery && (
          <div
            className="res-list"
            data-vaul-no-drag
            style={{ borderTop: "1px solid hsla(0 0% 100% / 0.08)" }}
          >
            {!discoveryRequested ? (
              <button
                type="button"
                onClick={() => setDiscoveryRequested(true)}
                onPointerDown={(e) => e.stopPropagation()}
                className="font-mono"
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  width: "100%",
                  padding: "12px 22px",
                  fontSize: 12,
                  color: "hsl(var(--as-fg-muted))",
                  background: "transparent",
                  border: "none",
                  cursor: "pointer",
                  textAlign: "left",
                }}
              >
                <Globe style={{ width: 14, height: 14, flexShrink: 0 }} />
                <span>
                  Search {discoveryInfo!.sourceCount} more source{discoveryInfo!.sourceCount === 1 ? "" : "s"}
                  <span style={{ opacity: 0.55, marginLeft: 8 }}>
                    from extensions you haven&apos;t installed — first search may take a while
                  </span>
                </span>
              </button>
            ) : isDiscoverySearching ? (
              <p
                className="font-mono"
                style={{
                  display: "flex",
                  alignItems: "center",
                  gap: 8,
                  padding: "12px 22px",
                  fontSize: 12,
                  color: "hsl(var(--as-fg-muted))",
                }}
              >
                <Loader2 className="h-4 w-4 animate-spin" style={{ flexShrink: 0 }} />
                <span>
                  Searching {discoveryInfo!.sourceCount} more source{discoveryInfo!.sourceCount === 1 ? "" : "s"}…
                  this can take several minutes the first time
                </span>
              </p>
            ) : discoveryError ? (
              <p
                className="flex items-center gap-2"
                style={{ color: "hsl(0 72% 51%)", fontSize: 12, padding: "12px 22px" }}
              >
                <AlertTriangle style={{ width: 14, height: 14, flexShrink: 0 }} />
                <span>{discoveryError.message}</span>
              </p>
            ) : discoverySeries.length === 0 ? (
              <p
                style={{
                  color: "hsl(var(--as-fg-muted))",
                  fontSize: 12,
                  padding: "12px 22px",
                }}
              >
                No additional results from not-installed sources
              </p>
            ) : (
              <>
                <p
                  className="font-mono"
                  style={{
                    fontSize: 11,
                    letterSpacing: "0.08em",
                    textTransform: "uppercase",
                    opacity: 0.55,
                    padding: "10px 22px 4px",
                  }}
                >
                  From sources not installed
                </p>
                {discoverySeries.map(renderSeriesRow)}
              </>
            )}
          </div>
        )}
    </div>
  );
}
