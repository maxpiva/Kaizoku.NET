"use client";

import React, { useState, useEffect, useRef, useCallback } from 'react';
import { ProviderManager } from "@/components/kzk/provider-manager";
import { type Provider } from "@/lib/api/types";

// Custom hook to detect if scrollbar is visible
function useScrollbarDetection() {
  const [hasScrollbar, setHasScrollbar] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const timeoutRef = useRef<NodeJS.Timeout>(null);

  useEffect(() => {
    const checkScrollbar = () => {
      if (containerRef.current) {
        const { scrollHeight, clientHeight } = containerRef.current;
        const newHasScrollbar = scrollHeight > clientHeight;
        setHasScrollbar(prev => prev !== newHasScrollbar ? newHasScrollbar : prev);
      }
    };

    const debouncedCheck = () => {
      if (timeoutRef.current) {
        clearTimeout(timeoutRef.current);
      }
      timeoutRef.current = setTimeout(checkScrollbar, 100);
    };

    checkScrollbar();
    
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

interface AddProvidersStepProps {
  setError: (error: string | null) => void;
  setIsLoading: (loading: boolean) => void;
  setCanProgress: (canProgress: boolean) => void;
}

export default function AddProvidersStep({
  setError,
  setIsLoading,
  setCanProgress
}: AddProvidersStepProps) {  const [searchTerm, setSearchTerm] = useState('');
  const { hasScrollbar, containerRef } = useScrollbarDetection();

  // Handle error with useCallback to prevent unnecessary re-renders
  const handleError = useCallback((error: string | null) => {
    setError(error);
  }, [setError]);

  // Handle loading change with useCallback to prevent unnecessary re-renders
  const handleLoadingChange = useCallback((loading: boolean) => {
    setIsLoading(loading);
  }, [setIsLoading]);

  // Handle extensions change to update progress
  const handleExtensionsChange = useCallback((extensions: Provider[]) => {
    const installedCount = extensions.filter(ext => ext.isInstaled).length;
    setCanProgress(installedCount > 0);
  }, [setCanProgress]);return (
    <div 
      ref={containerRef}
      className={`min-h-[90%] overflow-y-auto ${hasScrollbar ? 'pr-2' : ''}`}
    >
      <ProviderManager
        searchTerm={searchTerm}
        setSearchTerm={setSearchTerm}
        isCompact={true}
        showSearch={true}
        showNsfwIndicator={true}
        installedGridCols="grid-cols-1 sm:grid-cols-1 md:grid-cols-1 lg:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3"
        availableGridCols="grid-cols-1 sm:grid-cols-1 md:grid-cols-1 lg:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3"
        installedMaxHeight="min-h-20 max-h-60"
        availableMaxHeight="min-h-20 max-h-60"
        installedTitle="Installed Sources"
        availableTitle="Available Sources"
        description={
          <>
Install sources to access different websites. At least one source is required to continue. It's recommended to install enough sources to cover all your series.<br/>
After installation, adjust each source's settings. Be sure to choose whether each source is temporary or permanent, depending on how you plan to store the data.<br/>
If the storage folder was previously used in Kaizoku.NET, your previously installed sources will be restored automatically in the next step.
          </>
        }        onError={handleError}
        onLoadingChange={handleLoadingChange}
        onExtensionsChange={handleExtensionsChange}
      />
    </div>
  );
}

// Also export as named export for compatibility
export { AddProvidersStep };
