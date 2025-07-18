"use client";

import React, { createContext, useContext, useState, useCallback } from 'react';
import { usePathname } from 'next/navigation';
import { useDebounce } from '@/lib/hooks/useDebounce';

interface SearchContextType {
  searchTerm: string;
  debouncedSearchTerm: string;
  setSearchTerm: (term: string) => void;
  clearSearch: () => void;
  currentPage: 'library' | 'providers' | 'queue' | 'settings' | 'series' | 'other';
  isSearchDisabled: boolean;
}

const SearchContext = createContext<SearchContextType | undefined>(undefined);

export function SearchProvider({ children }: { children: React.ReactNode }) {
  // Initialize searchTerm from sessionStorage if available
  const [searchTerm, setSearchTermState] = useState(() => {
    if (typeof window === "undefined") return '';
    const saved = sessionStorage.getItem('kzk_search');
    return saved || '';
  });
  const pathname = usePathname();
  
  // Debounce search term for performance
  const debouncedSearchTerm = useDebounce(searchTerm, 300);  // Determine current page based on pathname
  const currentPage = React.useMemo(() => {
    if (pathname === '/' || pathname === '/library') return 'library';
    if (pathname === '/providers') return 'providers';
    if (pathname === '/queue') return 'queue';
    if (pathname === '/settings' || pathname.startsWith('/library/settings')) return 'settings';
    if (pathname.startsWith('/library/series')) return 'series';
    return 'other';
  }, [pathname]);

  // Determine if search should be disabled
  const isSearchDisabled = React.useMemo(() => {
    return currentPage === 'settings' || currentPage === 'series';
  }, [currentPage]);

  const setSearchTerm = useCallback((term: string) => {
    setSearchTermState(term);
    // Persist to sessionStorage
    if (typeof window !== "undefined") {
      sessionStorage.setItem('kzk_search', term);
    }
  }, []);

  const clearSearch = useCallback(() => {
    setSearchTermState('');
    // Clear from sessionStorage
    if (typeof window !== "undefined") {
      sessionStorage.setItem('kzk_search', '');
    }
  }, []);
  return (
    <SearchContext.Provider
      value={{
        searchTerm,
        debouncedSearchTerm,
        setSearchTerm,
        clearSearch,
        currentPage,
        isSearchDisabled,
      }}
    >
      {children}
    </SearchContext.Provider>
  );
}

export function useSearch() {
  const context = useContext(SearchContext);
  if (context === undefined) {
    throw new Error('useSearch must be used within a SearchProvider');
  }
  return context;
}
