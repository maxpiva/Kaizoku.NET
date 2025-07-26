"use client";

// All rendering happens on the client for static export compatibility

import { ListFilter } from "lucide-react";
import { AddSeries } from "@/components/kzk/series/add-series";
import { ListSeries } from "@/components/kzk/series/list-series";
import { Button } from "@/components/ui/button";
import {
  Select,
  SelectTrigger,
  SelectContent,
  SelectItem,
  SelectValue,
  SelectLabel,
  SelectSeparator,
} from "@/components/ui/select";
import KzkHeader from "@/components/kzk/layout/header";
import KzkSidebar from "@/components/kzk/layout/sidebar";
import { useState, useMemo, useEffect, useCallback } from "react";
import { SeriesStatus, type SeriesInfo } from "@/lib/api/types";
import { useLibrary } from "@/lib/api/hooks/useSeries";

export default function RootPage() {
  // Session storage keys
  const SESSION_KEYS = {
    tab: "kzk_tab",
    genre: "kzk_genre",
    provider: "kzk_provider",
    orderBy: "kzk_orderBy",
    cardWidth: "kzk_cardWidth",
  };

  // Read initial values from sessionStorage
  function getSessionValue(key: string, fallback: string | null): string | null {
    if (typeof window === "undefined") return fallback;
    const value = sessionStorage.getItem(key);
    return value !== null && value !== "" ? value : fallback;
  }

  const [tab, setTabState] = useState<string>(getSessionValue(SESSION_KEYS.tab, "all")!);
  const [selectedGenre, setSelectedGenreState] = useState<string | null>(getSessionValue(SESSION_KEYS.genre, null));
  const [selectedProvider, setSelectedProviderState] = useState<string | null>(getSessionValue(SESSION_KEYS.provider, null));
  const [orderBy, setOrderByState] = useState<string>(getSessionValue(SESSION_KEYS.orderBy, "title")!);
  const [cardWidth, setCardWidthState] = useState<string>(getSessionValue(SESSION_KEYS.cardWidth, "w-45")!);

  // Wrap setters to also update sessionStorage
  const setTab = (v: string) => { setTabState(v); sessionStorage.setItem(SESSION_KEYS.tab, v); };
  const setSelectedGenre = (v: string | null) => { setSelectedGenreState(v); sessionStorage.setItem(SESSION_KEYS.genre, v ?? ""); };
  const setSelectedProvider = (v: string | null) => { setSelectedProviderState(v); sessionStorage.setItem(SESSION_KEYS.provider, v ?? ""); };
  const setOrderBy = (v: string) => { setOrderByState(v); sessionStorage.setItem(SESSION_KEYS.orderBy, v); };
  const setCardWidth = (v: string) => { setCardWidthState(v); sessionStorage.setItem(SESSION_KEYS.cardWidth, v); };

  const { data: library } = useLibrary();

  // Debug and deduplicate library data to prevent duplicate keys
  const deduplicatedLibrary = useMemo(() => {
    if (!library) return library;
    
    // Check for duplicates and log them
    const seen = new Set<string>();
    const duplicates: string[] = [];
    const unique: SeriesInfo[] = [];
    
    library.forEach((series) => {
      if (seen.has(series.id)) {
        duplicates.push(series.title);
        console.warn(`[Library] Duplicate series detected: ${series.title} (ID: ${series.id})`);
      } else {
        seen.add(series.id);
        unique.push(series);
      }
    });
    
    if (duplicates.length > 0) {
      console.error(`[Library] Found ${duplicates.length} duplicate series:`, duplicates);
    }
    
    return unique;
  }, [library]);

  const cardWidthOptions = [
    { value: "w-20", label: "XS", text: "text-[0.4rem]" },
    { value: "w-32", label: "S", text: "text-xs" },
    { value: "w-45", label: "M", text: "text-sm" },
    { value: "w-58", label: "L", text: "text-base" },
    { value: "w-70", label: "XL", text: "text-lg" },
  ];

  // Compute unique sorted genres from all series
  const genres = useMemo(() => {
    if (!deduplicatedLibrary) return [];
    const genreSet = new Set<string>();
    deduplicatedLibrary.forEach((series) => {
      series.genre?.forEach((g) => genreSet.add(g));
    });
    return Array.from(genreSet).sort((a, b) => a.localeCompare(b));
  }, [deduplicatedLibrary]);

  // Compute unique sorted providers from all series
  const providers = useMemo(() => {
    if (!deduplicatedLibrary) return [];
    const providerSet = new Set<string>();
    deduplicatedLibrary.forEach((series) => {
      series.providers?.forEach((p) => providerSet.add(p.provider));
    });
    return Array.from(providerSet).sort((a, b) => a.localeCompare(b));
  }, [deduplicatedLibrary]);

  // Sorting logic for ListSeries - memoized for performance
  const sortFn = useCallback((a: SeriesInfo, b: SeriesInfo) => {
    if (orderBy === "lastChange") {
      // Descending by lastChangeUTC
      return (b.lastChangeUTC ? new Date(b.lastChangeUTC).getTime() : 0) - (a.lastChangeUTC ? new Date(a.lastChangeUTC).getTime() : 0);
    }
    // Default: Alphabetical by title
    return a.title.localeCompare(b.title);
  }, [orderBy]);

  // Filtering logic for ListSeries - memoized for performance
  const filterFn = useCallback((series: SeriesInfo) => {
    const matchesTab =
      tab === "completed"
        ? series.status === SeriesStatus.COMPLETED || series.status === SeriesStatus.PUBLISHING_FINISHED 
        : tab === "active"
        ? series.status !== SeriesStatus.COMPLETED && series.status !== SeriesStatus.PUBLISHING_FINISHED && series.isActive && !series.pausedDownloads
        : tab === "paused"
        ? series.pausedDownloads
        : tab === "unassigned"
        ? series.hasUnknown === true
        : true;
    const matchesGenre = selectedGenre ? series.genre?.includes(selectedGenre) : true;
    const matchesProvider = selectedProvider ? series.providers?.some((p) => p.provider === selectedProvider) : true;
    return matchesTab && matchesGenre && matchesProvider;
  }, [tab, selectedGenre, selectedProvider]);

  // Count for each tab (with genre and provider filter applied) - memoized for performance
  const { allCount, activeCount, pausedCount, unassignedCount, completedCount } = useMemo(() => {
    if (!deduplicatedLibrary) return { allCount: 0, activeCount: 0, pausedCount: 0, unassignedCount: 0, completedCount: 0 };
    
    // Base filter function for genre and provider
    const baseFilter = (series: SeriesInfo) => 
      (!selectedGenre || series.genre?.includes(selectedGenre)) &&
      (!selectedProvider || series.providers?.some((p) => p.provider === selectedProvider));
    
    const baseFiltered = deduplicatedLibrary.filter(baseFilter);
    
    return {
      allCount: baseFiltered.length,
      activeCount: baseFiltered.filter(series => 
        series.status !== SeriesStatus.COMPLETED && 
        series.status !== SeriesStatus.PUBLISHING_FINISHED && 
        series.isActive && 
        !series.pausedDownloads
      ).length,
      pausedCount: baseFiltered.filter(series => series.pausedDownloads).length,
      unassignedCount: baseFiltered.filter(series => series.hasUnknown === true).length,
      completedCount: baseFiltered.filter(series => 
        series.status === SeriesStatus.COMPLETED || 
        series.status === SeriesStatus.PUBLISHING_FINISHED
      ).length,
    };
  }, [deduplicatedLibrary, selectedGenre, selectedProvider]);

  return (
    <div className="flex min-h-screen w-full flex-col bg-muted/40">
      <KzkSidebar />
      <div className="flex flex-col sm:gap-4 sm:py-4 sm:pl-14">
        <KzkHeader />
        <main className="grid flex-1 items-start gap-4 p-4 sm:px-6 sm:py-0">
                <div className="flex items-center">
                  {/* Status Filter - first select */}
                  <div className="w-40">
                    <Select value={tab} onValueChange={setTab}>
                      <SelectTrigger className="w-full !pr-2 caret-transparent">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="all">
                          All{allCount > 0 && ` (${allCount})`}
                        </SelectItem>
                        <SelectItem value="active">
                          Active{activeCount > 0 && ` (${activeCount})`}
                        </SelectItem>
                        <SelectItem value="paused">
                          Paused{pausedCount > 0 && ` (${pausedCount})`}
                        </SelectItem>
                        <SelectItem value="unassigned">
                          Unassigned{unassignedCount > 0 && ` (${unassignedCount})`}
                        </SelectItem>
                        <SelectItem value="completed">
                          Completed{completedCount > 0 && ` (${completedCount})`}
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                 
                    <div className="w-40 ml-2">
                      <Select
                        value={selectedGenre ?? "__ALL__"}
                        onValueChange={(value) => setSelectedGenre(value === "__ALL__" ? null : value)}
                      >
                        <SelectTrigger className="w-full !pr-2 caret-transparent">
                          <SelectValue placeholder="All Genres" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="__ALL__">All Genres</SelectItem>
                          {genres.map((genre) => (
                            <SelectItem key={genre} value={genre}>
                              {genre}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                    <div className="w-48 ml-2">
                      <Select
                        value={selectedProvider ?? "__ALL__"}
                        onValueChange={(value) => setSelectedProvider(value === "__ALL__" ? null : value)}
                      >
                        <SelectTrigger className="w-full !pr-2 caret-transparent">
                          <SelectValue placeholder="All Sources" />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="__ALL__">All Sources</SelectItem>
                          {providers.map((provider) => (
                            <SelectItem key={provider} value={provider}>
                              {provider}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                  <div className="ml-auto flex items-center gap-2">
                     {/* Order Select - immediately after tabs, to the left */}
                  <div className="w-32">
                    <Select value={orderBy} onValueChange={setOrderBy}>
                      <SelectTrigger className="w-full !pr-2 caret-transparent">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="title">Alphabetical</SelectItem>
                        <SelectItem value="lastChange">Last Change</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                  {/* Card Size Select */}
                    <div className="w-16">
                      <Select value={cardWidth} onValueChange={setCardWidth}>
                        <SelectTrigger className="w-full !pr-2 caret-transparent">
                          <SelectValue placeholder="Card Size" />
                        </SelectTrigger>
                        <SelectContent>
                          {cardWidthOptions.map(opt => (
                            <SelectItem key={opt.value} value={opt.value}>{opt.label}</SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>

                    <div className="h-9">
                      <AddSeries />
                    </div>
                  </div>
                </div>
                <div className="flex flex-wrap gap-4 pt-4">
                  <ListSeries 
                    filterFn={filterFn} 
                    sortFn={sortFn} 
                    cardWidth={cardWidth} 
                    cardWidthOptions={cardWidthOptions} 
                    orderBy={orderBy}
                    library={deduplicatedLibrary}
                  />
                </div>
        </main>
      </div>
    </div>
  );
}
