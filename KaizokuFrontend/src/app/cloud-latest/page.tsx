"use client";

import React, { useState, useMemo, useEffect, useCallback, useRef } from 'react';
import { Sparkles } from "lucide-react";
import {
  Select,
  SelectTrigger,
  SelectContent,
  SelectItem,
  SelectValue,
} from "@/components/ui/select";
import { useSearch } from "@/contexts/search-context";
import { useSearchSources, useLatest } from "@/lib/api/hooks/useSeries";
import { seriesService } from "@/lib/api/services/seriesService";
import { useQueryClient } from '@tanstack/react-query';
import { CloudLatestGrid } from "@/components/kzk/series/cloud-latest-grid";
import { type LatestSeriesInfo } from "@/lib/api/types";
import { useDebounce } from "@/lib/hooks/useDebounce";

const ITEMS_PER_PAGE = 40; // Increased to ensure screen fill

// Calculate optimal items per page based on card width and screen size
function calculateItemsPerPage(cardWidth: string): number {
  // Card width mappings (in rem, then converted to px)
  const cardWidths: Record<string, number> = {
    "w-20": 5 * 16,    // 80px
    "w-32": 8 * 16,    // 128px  
    "w-45": 11.25 * 16, // 180px
    "w-58": 14.5 * 16,  // 232px
    "w-70": 17.5 * 16,  // 280px
  };

  const cardWidthPx = cardWidths[cardWidth] || 180; // Default to medium
  const gap = 16; // 1rem gap
  const aspectRatio = 4/6; // Card aspect ratio
  const cardHeight = cardWidthPx / aspectRatio; // ~270px for medium

  // Estimate screen dimensions
  const screenWidth = typeof window !== "undefined" ? window.innerWidth : 1920;
  const screenHeight = typeof window !== "undefined" ? window.innerHeight : 1080;
  
  // Account for sidebar, padding, header (roughly 300px total)
  const availableWidth = screenWidth - 300;
  const availableHeight = screenHeight - 200;
  
  // Calculate columns and rows that fit on screen
  const columns = Math.floor((availableWidth + gap) / (cardWidthPx + gap));
  const rows = Math.floor((availableHeight + gap) / (cardHeight + gap));
  
  // We want to fetch 2-3 screens worth to ensure infinite scroll works
  const itemsPerScreen = Math.max(columns * rows, 12); // Minimum 12 items
  const optimalFetch = Math.max(itemsPerScreen * 2, 40); // At least 2 screens worth, minimum 40
  
  return optimalFetch;
}

export default function CloudLatestPage() {
  // Session storage keys
  const SESSION_KEYS = {
    sourceId: "kzk_cloud_sourceId",
    cardWidth: "kzk_cloud_cardWidth",
    search: "kzk_cloud_search",
  };

  // Read initial values from sessionStorage
  function getSessionValue(key: string, fallback: string | null): string | null {
    if (typeof window === "undefined") return fallback;
    const value = sessionStorage.getItem(key);
    return value !== null && value !== "" ? value : fallback;
  }

  const [selectedSourceId, setSelectedSourceIdState] = useState<string | null>(
    getSessionValue(SESSION_KEYS.sourceId, null)
  );
  const [cardWidth, setCardWidthState] = useState<string>(getSessionValue(SESSION_KEYS.cardWidth, "w-45")!);
  const [items, setItems] = useState<LatestSeriesInfo[]>([]);
  const [currentPage, setCurrentPage] = useState(0);
  const [hasMore, setHasMore] = useState(true);
  const [isLoadingMore, setIsLoadingMore] = useState(false);

  // Track user activity for periodic refresh logic
  const lastActivityRef = useRef<number>(Date.now());
  const lastLatestDataRef = useRef<LatestSeriesInfo[] | null>(null);
  const queryClient = useQueryClient();

  // Calculate dynamic items per page based on card size
  const itemsPerPage = useMemo(() => {
    return calculateItemsPerPage(cardWidth);
  }, [cardWidth]);

  // Debounce card width changes to prevent race conditions
  const debouncedCardWidth = useDebounce(cardWidth, 300);

  // Wrap setters to also update sessionStorage
  const setSelectedSourceId = (v: string | null) => {
    setSelectedSourceIdState(v);
    sessionStorage.setItem(SESSION_KEYS.sourceId, v ?? "");
  };

  const setCardWidth = (v: string) => {
    setCardWidthState(v);
    sessionStorage.setItem(SESSION_KEYS.cardWidth, v);
  };

  const { debouncedSearchTerm } = useSearch();
  const { data: sources } = useSearchSources();

  // Card width options (same as main page)
  const cardWidthOptions = [
    { value: "w-20", label: "XS", text: "text-[0.4rem]" },
    { value: "w-32", label: "S", text: "text-xs" },
    { value: "w-45", label: "M", text: "text-sm" },
    { value: "w-58", label: "L", text: "text-base" },
    { value: "w-70", label: "XL", text: "text-lg" },
  ];

  // Sync search box value in sessionStorage
  useEffect(() => {
    if (typeof window === "undefined") return;
    const searchInput = document.querySelector<HTMLInputElement>(
      "input[type='search'], input[type='text'][placeholder*='search']"
    );
    if (!searchInput) return;
    // Set initial value
    const saved = sessionStorage.getItem(SESSION_KEYS.search);
    if (saved && searchInput.value !== saved) searchInput.value = saved;
    // Save on change
    const handler = (e: Event) => {
      const target = e.target as HTMLInputElement;
      sessionStorage.setItem(SESSION_KEYS.search, target.value);
    };
    searchInput.addEventListener("input", handler);
    return () => searchInput.removeEventListener("input", handler);
  }, [SESSION_KEYS.search]);

  // Reset pagination when filters change (but NOT for card width changes)
  useEffect(() => {
    setItems([]);
    setCurrentPage(0);
    setHasMore(true);
  }, [debouncedSearchTerm, selectedSourceId]); // Removed debouncedCardWidth

  // Calculate dynamic items per page based on debounced card size for API calls
  const debouncedItemsPerPage = useMemo(() => {
    return calculateItemsPerPage(debouncedCardWidth);
  }, [debouncedCardWidth]);

  // Fetch latest series data
  const { data: latestData, isLoading, error } = useLatest(
    currentPage * debouncedItemsPerPage,
    debouncedItemsPerPage,
    selectedSourceId ?? undefined,
    debouncedSearchTerm ?? undefined,
    true
  );

  // Check if we need to load more items when card size changes
  useEffect(() => {
    if (!items.length) return; // Don't trigger on initial load or filter changes
    
    const currentItemsOnScreen = items.length;
    const newRequiredItems = debouncedItemsPerPage;
    
    // If we need more items to fill the screen and we have more available
    if (currentItemsOnScreen < newRequiredItems && hasMore && !isLoading && !isLoadingMore) {
      setIsLoadingMore(true);
      setCurrentPage(prev => prev + 1);
    }
  }, [debouncedItemsPerPage, items.length, hasMore, isLoading, isLoadingMore]);

  // Track user activity (mouse, keyboard, touch events)
  useEffect(() => {
    const updateActivity = () => {
      lastActivityRef.current = Date.now();
    };

    const events = ['mousedown', 'keypress', 'scroll', 'touchstart', 'click'];
    
    events.forEach(event => {
      document.addEventListener(event, updateActivity, true);
    });

    return () => {
      events.forEach(event => {
        document.removeEventListener(event, updateActivity, true);
      });
    };
  }, []);

  // Periodic refresh of latest data when user is idle
  useEffect(() => {
    const interval = setInterval(async () => {
      const now = Date.now();
      const timeSinceLastActivity = now - lastActivityRef.current;
      const oneMinuteInMs = 60 * 1000;
      
      // Only refresh if user has been idle for at least 1 minute
      if (timeSinceLastActivity < oneMinuteInMs) return;

      try {
        // Get fresh data from server for the first page only
        const freshLatestData = await seriesService.getLatest(
          0, // Always refresh first page
          debouncedItemsPerPage,
          selectedSourceId ?? undefined,
          debouncedSearchTerm ?? undefined
        );
        
        // Compare with previous data using memo-like logic
        const hasChanges = !lastLatestDataRef.current || 
          JSON.stringify(lastLatestDataRef.current) !== JSON.stringify(freshLatestData);

        if (hasChanges) {
          // Update the query cache with fresh data
          queryClient.setQueryData(['series', 'latest', {
            offset: 0,
            limit: debouncedItemsPerPage,
            sourceId: selectedSourceId ?? undefined,
            searchTerm: debouncedSearchTerm ?? undefined
          }], freshLatestData);
          
          // Store the new data for next comparison
          lastLatestDataRef.current = freshLatestData;
          
          console.log('Latest data refreshed due to changes detected (user idle)');
        }
      } catch (error) {
        console.error('Failed to refresh latest data:', error);
      }
    }, 60000); // Check every 60 seconds

    return () => clearInterval(interval);
  }, [selectedSourceId, debouncedSearchTerm, debouncedItemsPerPage, queryClient]);

  // Store latest data for comparison on each update
  useEffect(() => {
    if (latestData && currentPage === 0) {
      lastLatestDataRef.current = latestData;
    }
  }, [latestData, currentPage]);

  // Update items when new data arrives
  useEffect(() => {
    if (latestData) {
      if (currentPage === 0) {
        // First page - replace all items
        setItems(latestData);
      } else {
        // Subsequent pages - append items
        setItems(prevItems => [...prevItems, ...latestData]);
      }
      
      // Since the Latest endpoint doesn't provide metadata about total count,
      // we infer hasMore from the response size:
      // - If we get exactly debouncedItemsPerPage items, there are likely more
      // - If we get fewer than debouncedItemsPerPage items, we've reached the end
      setHasMore(latestData.length >= debouncedItemsPerPage);
      setIsLoadingMore(false);
    }
  }, [latestData, currentPage, debouncedItemsPerPage]);

  // Load more function for infinite scroll
  const loadMore = useCallback(() => {
    if (!hasMore || isLoading || isLoadingMore) return;
    
    setIsLoadingMore(true);
    setCurrentPage(prev => prev + 1);
  }, [hasMore, isLoading, isLoadingMore]);

  // Sorted sources for the select
  const sortedSources = useMemo(() => {
    if (!sources) return [];
    return [...sources].sort((a, b) => a.sourceName.localeCompare(b.sourceName));
  }, [sources]);

  return (
    <>
      <div className="flex items-center">
        <div className="w-48">
            <Select
              value={selectedSourceId ?? "__ALL__"}
              onValueChange={(value) => setSelectedSourceId(value === "__ALL__" ? null : value)}
            >
              <SelectTrigger className="w-full !pr-2 caret-transparent">
                <SelectValue placeholder="All Sources" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="__ALL__">All Sources</SelectItem>
                {sortedSources.map((source) => (
                  <SelectItem key={source.sourceId} value={source.sourceId}>
                    {source.sourceName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        
        
        <div className="ml-auto flex items-center gap-2">
          {/* Card Size Select - immediately after title, to the left */}
        <div className="ml-4 w-16">
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
        </div>
      </div>

      <div className="pt-4">
        <CloudLatestGrid
          items={items}
          isLoading={isLoading && currentPage === 0}
          isLoadingMore={isLoadingMore}
          hasMore={hasMore}
          onLoadMore={loadMore}
          error={error}
          cardWidth={cardWidth}
          cardWidthOptions={cardWidthOptions}
        />
      </div>
    </>
  );
}
