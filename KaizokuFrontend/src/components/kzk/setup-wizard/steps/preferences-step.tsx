"use client";

import React, { useEffect, useState, useRef } from 'react';
import { SettingsManager } from "@/components/kzk/settings-manager";
import { useSetupWizard } from "@/components/providers/setup-wizard-provider";
import type { Settings } from "@/lib/api/types";

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

interface PreferencesStepProps {
  setError: (error: string | null) => void;
  setIsLoading: (loading: boolean) => void;
  setCanProgress: (canProgress: boolean) => void;
}

export function PreferencesStep({
  setError,
  setIsLoading,
  setCanProgress
}: PreferencesStepProps) {
  const { setStepData, getStepData } = useSetupWizard();
  const { hasScrollbar, containerRef } = useScrollbarDetection();

  // Get any previously saved preferences from wizard state
  const savedPreferences = getStepData(0) as Settings | undefined;

  useEffect(() => {
    // Always allow progressing from preferences step
    setCanProgress(true);
    setError(null);
    setIsLoading(false);
  }, [setCanProgress, setError, setIsLoading]);

  const handleSettingsChange = (settings: Settings) => {
    // Save preferences to wizard state
    setStepData(0, settings);
  };

  const handleSave = (settings: Settings) => {
    // Save preferences to wizard state
    setStepData(0, settings);
    // Note: Settings will be actually saved to backend when next step is triggered
  };  return (
    <div className="space-y-4 min-w-0 overflow-hidden">
      <div className="text-sm text-muted-foreground">
        Configure your content preferences, download settings, and other preferences.
        These settings can be changed later in the Settings page.
      </div>      <div
        ref={containerRef}
        className={`max-h-[50vh] sm:max-h-[60vh] overflow-y-auto overflow-x-hidden ${hasScrollbar ? 'pr-2' : ''}`}
      >
        <SettingsManager
          sections={[
            'content-preferences',
            'mihon-repositories',
            'download-settings',
            'schedule-tasks',
            'storage',
            'flaresolverr'
          ]}
          showHeader={false}
          showSaveButton={false}
          useLocalState={true}
          initialSettings={savedPreferences}
          onSettingsChange={handleSettingsChange}
          onSave={handleSave}
          className="border-0 bg-transparent p-0"
        />
      </div>
    </div>
  );
}
