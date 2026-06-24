"use client";

import { useSearchParams, useRouter } from "next/navigation";
import { useState, useEffect, useRef, Suspense } from "react";
import { useSeriesById, useDeleteSeries, useUpdateSeries, useVerifyIntegrity, useCleanupSeries, useRefreshSeries, useRenameSeries } from "@/lib/api/hooks/useSeries";
import { useToast } from "@/hooks/use-toast";
import { seriesService } from "@/lib/api/services/seriesService";
import { useQueryClient } from '@tanstack/react-query';
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Trash2, ShieldCheck, FolderSync } from "lucide-react";
import { SeriesStatus, ArchiveResult, type ExistingSource, type SeriesExtendedInfo, type SeriesIntegrityResult } from "@/lib/api/types";
import { useSeriesContext } from "@/contexts/series-context";

import { usePermission } from "@/hooks/use-permission";

import { DownloadsPanel } from "@/components/comp/series/detail/downloads-panel";
import { SeriesHero } from "@/components/comp/series/detail/series-hero";
import { SourcesSection } from "@/components/comp/series/detail/sources-section";
import { ChaptersSection } from "@/components/comp/series/detail/chapters-section";
import { SeriesRibbon } from "@/components/comp/series/detail/series-ribbon";



export default function SeriesPage() {
  return (
    <Suspense fallback={<div className="flex items-center justify-center h-64">Loading...</div>}>
      <SeriesPageContent />
    </Suspense>
  );
}

function SeriesPageContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const seriesId = searchParams.get('id');
  const { setSeriesTitle } = useSeriesContext();
  const queryClient = useQueryClient();

  // Permissions
  const canEdit = usePermission('canEditSeries');
  const canDelete = usePermission('canDeleteSeries');
  const canManageDownloads = usePermission('canManageDownloads');

  // Track deletion state to prevent loops
  const [isDeleting, setIsDeleting] = useState(false);

  const { data: series, isLoading, error, refetch } = useSeriesById(seriesId || '', !isDeleting);
  const deleteSeries = useDeleteSeries();
  const updateSeriesMutation = useUpdateSeries();
  const verifyIntegrity = useVerifyIntegrity();
  const cleanupSeries = useCleanupSeries();
  const refreshSeries = useRefreshSeries();
  const renameSeries = useRenameSeries();
  const { toast } = useToast();
  
  // Provider switch state management
  const [providerSwitches, setProviderSwitches] = useState<Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }>>({});

  // Provider disabled state management  
  const [providerDisabledStates, setProviderDisabledStates] = useState<Record<string, boolean>>({});  // Provider deleted state management  
  const [providerDeletedStates, setProviderDeletedStates] = useState<Record<string, boolean>>({});

  // Pause downloads state management
  const [pausedDownloads, setPausedDownloads] = useState<boolean>(false);  // Provider fromChapter state management
  const [providerFromChapters, setProviderFromChapters] = useState<Record<string, string>>({});

  // Delete dialog state management
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [deletePhysicalFiles, setDeletePhysicalFiles] = useState(false);
  
  // Verify integrity dialog state management
  const [showVerifyDialog, setShowVerifyDialog] = useState(false);
  const [verifyResult, setVerifyResult] = useState<SeriesIntegrityResult | null>(null);
  const [showCleanupDialog, setShowCleanupDialog] = useState(false);

  // Rename (fix folder + .cbz names) confirmation dialog state
  const [showRenameDialog, setShowRenameDialog] = useState(false);
  
  // Track user activity for periodic refresh logic
  const lastActivityRef = useRef<number>(Date.now());
  const lastSeriesDataRef = useRef<SeriesExtendedInfo | null>(null);
  
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

  // Periodic refresh of series data when user is idle
  useEffect(() => {
    if (!seriesId || isDeleting) return;

    const interval = setInterval(async () => {
      const now = Date.now();
      const timeSinceLastActivity = now - lastActivityRef.current;
      const oneMinuteInMs = 60 * 1000;
      
      // Only refresh if user has been idle for at least 1 minute
      if (timeSinceLastActivity < oneMinuteInMs) return;

      try {
        // Get fresh data from server
        const freshSeriesData = await seriesService.getSeriesById(seriesId);
        
        // Compare with previous data using memo-like logic
        const hasChanges = !lastSeriesDataRef.current || 
          JSON.stringify(lastSeriesDataRef.current) !== JSON.stringify(freshSeriesData);

        if (hasChanges) {
          // Update the query cache with fresh data
          queryClient.setQueryData(['series', 'detail', seriesId], freshSeriesData);
          
          // Store the new data for next comparison
          lastSeriesDataRef.current = freshSeriesData;
          
          console.log('Series data refreshed due to changes detected (user idle)');
        }
      } catch (error) {
        console.error('Failed to refresh series data:', error);
      }
    }, 60000); // Check every 60 seconds

    return () => clearInterval(interval);
  }, [seriesId, isDeleting, queryClient]);

  // Store series data for comparison on each update
  useEffect(() => {
    if (series && !isDeleting) {
      lastSeriesDataRef.current = series;
    }
  }, [series, isDeleting]);
  
  // Helper function that allows updating with an explicit pausedDownloads value
  const updateSeriesWithPausedDownloads = async (overridePausedDownloads: boolean) => {
    if (!series || isDeleting) return;
    
    try {
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: overridePausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          const fromChapterValue = providerFromChapters[provider.id];
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          return updatedProvider;
        })
      };

      // Check if any provider is deleted
      const hasDeletedProviders = updatedSeries.providers.some(p => p.isDeleted);

      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend
      // This ensures that if the backend changed any values (like fromChapter), the UI reflects them
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
        });
        
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation to prevent unnecessary refetching
      // Update the React Query cache directly with the result from the backend
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Library invalidation is now handled by the useUpdateSeries hook automatically
      
      // Only invalidate if provider count actually changed (to handle edge cases)
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }
    } catch (error) {
      console.error('Failed to update series:', error);
    }
  };

  // Standard update series function (uses current pausedDownloads state)
  const updateSeries = async () => {
    // Call the override function with the current pausedDownloads value
    await updateSeriesWithPausedDownloads(pausedDownloads);
  };

  // Generic update function that accepts explicit switch values
  const updateSeriesWithSwitches = async (providerId: string, newSwitches: { useTitle?: boolean; useCover?: boolean; useStorage?: boolean }) => {
    if (!series || isDeleting) return;
    
    try {
      // Create a temporary modified switches object with the new values
      const effectiveSwitches = {
        ...providerSwitches,
        [providerId]: {
          ...providerSwitches[providerId],
          ...newSwitches
        }
      };
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          // Use the modified switches for the specific provider
          const currentSwitches = provider.id === providerId ? 
            effectiveSwitches[providerId] : 
            (providerSwitches[provider.id] || { useTitle: false, useCover: false, useStorage: false });
          const fromChapterValue = providerFromChapters[provider.id];
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: currentSwitches?.useTitle ?? provider.useTitle,
            useCover: currentSwitches?.useCover ?? provider.useCover,
            isStorage: currentSwitches?.useStorage ?? provider.isStorage,
          };
          
          
          return updatedProvider;
        })
      };



      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend if needed
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        const updatedSwitches: Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
          // Update switches with backend response
          updatedSwitches[provider.id] = {
            useTitle: provider.useTitle ?? false,
            useCover: provider.useCover ?? false,
            useStorage: provider.isStorage ?? false
          };
        });
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Update the switches with values from backend response
        setProviderSwitches(prev => ({
          ...prev,
          ...updatedSwitches
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Always invalidate library cache when series data changes (for tab filtering)
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'library'] 
      });
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with switches:', error);
    }
  };

  // Helper function to update with explicit disabled state
  const updateSeriesWithDisabledState = async (providerId: string, disabledState: boolean) => {
    if (!series || isDeleting) return;
    
    try {
      // Create a temporary modified disabled states object with the new value
      const effectiveDisabledStates = {
        ...providerDisabledStates,
        [providerId]: disabledState
      };
      
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          const fromChapterValue = providerFromChapters[provider.id];
          // Use the modified disabled state for the specific provider
          const currentDisabledState = provider.id === providerId ? 
            disabledState : 
            (providerDisabledStates[provider.id] ?? provider.isDisabled);
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : currentDisabledState,
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          
          
          return updatedProvider;
        })
      };


      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend if needed
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
        });
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Always invalidate library cache when series data changes (for tab filtering)
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'library'] 
      });
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with disabled state:', error);
    }
  };

  // Helper function to update with explicit deleted state
  const updateSeriesWithDeletedState = async (providerId: string, deletedState: boolean) => {
    if (!series || isDeleting) return;
    
    try {
      // Create a temporary modified deleted states object with the new value
      const effectiveDeletedStates = {
        ...providerDeletedStates,
        [providerId]: deletedState
      };
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          const fromChapterValue = providerFromChapters[provider.id];
          // Use the modified deleted state for the specific provider
          const currentDeletedState = provider.id === providerId ? 
            deletedState : 
            (providerDeletedStates[provider.id] ?? false);
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: currentDeletedState,
            fromChapter: fromChapterValue !== undefined ? parseFloat(fromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          
          return updatedProvider;
        })
      };


      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend if needed
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        const updatedDeletedStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
          // Update deleted states with backend response
          updatedDeletedStates[provider.id] = provider.isDeleted ?? false;
        });
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Update the deleted states with values from backend response
        setProviderDeletedStates(prev => ({
          ...prev,
          ...updatedDeletedStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }

      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Always invalidate library cache when series data changes (for tab filtering)
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'library'] 
      });
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with deleted state:', error);
    }
  };

  // Cleanup effect to handle component unmount during deletion
  useEffect(() => {
    return () => {
      // If component is unmounting and we're deleting, cancel any ongoing queries
      if (isDeleting && seriesId) {
        queryClient.cancelQueries({ queryKey: ['series', 'detail', seriesId] });
        queryClient.cancelQueries({ queryKey: ['downloads', 'series', seriesId] });
      }
    };
  }, [isDeleting, seriesId, queryClient]);

  // Update series title in context when data loads
  useEffect(() => {
    // Don't update title if we're deleting
    if (isDeleting) {
      return;
    }
    
    if (series?.title) {
      setSeriesTitle(series.title);
    } else if (!seriesId) {
      setSeriesTitle('Error');
    } else if (isLoading) {
      setSeriesTitle('Loading...');
    } else if (error || !series) {
      setSeriesTitle('Error');
    }
  }, [series, isLoading, error, seriesId, setSeriesTitle, isDeleting]);  // Initialize provider switches from the series data (only when local state is empty)
  useEffect(() => {
    // Don't initialize if we're deleting
    if (isDeleting) {
      return;
    }
    
    if (series && series.providers) {
      // Only initialize if local state is empty (first load or series change)
      const hasLocalState = Object.keys(providerSwitches).length > 0;
      const seriesIdChanged = series.id !== searchParams.get('id');
      
      if (!hasLocalState || seriesIdChanged) {
        const initialSwitches: Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }> = {};
        const initialDisabledStates: Record<string, boolean> = {};
        const initialFromChapters: Record<string, string> = {};
        
        series.providers.forEach(provider => {
          const hasUnknownThumbnail = provider.thumbnailUrl?.toLowerCase().includes('unknown') ?? false;
          initialSwitches[provider.id] = {
            useTitle: provider.useTitle ?? false,
            useCover: hasUnknownThumbnail ? false : (provider.useCover ?? false),
            useStorage: provider.isStorage ?? false
          };
          // If provider is uninstalled, it should be treated as disabled
          initialDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
          initialFromChapters[provider.id] = provider.fromChapter?.toString() || "";
        });

        setProviderSwitches(initialSwitches);
        setProviderDisabledStates(initialDisabledStates);
        setProviderFromChapters(initialFromChapters);
        setPausedDownloads(series.pausedDownloads ?? false);
      }
    }
  }, [series, providerSwitches, searchParams, isDeleting]);
  // Compute derived values (only when series is available)
  const activeProviderForTitle = series?.providers.find(p =>
    !providerDeletedStates[p.id] && providerSwitches[p.id]?.useTitle
  );
  const activeProviderForCover = series?.providers.find(p =>
    !providerDeletedStates[p.id] && providerSwitches[p.id]?.useCover
  );// Check if all providers are disabled (including UI state changes, excluding unknown, uninstalled, and deleted providers)
  const knownProviders = series?.providers?.filter(provider =>
    !provider.isUnknown &&
    !provider.isUninstalled &&
    !providerDeletedStates[provider.id]
  ) || [];
  const allProvidersDisabled = knownProviders.length > 0 && knownProviders.every(provider => {
    const isDisabled = providerDisabledStates[provider.id] ?? provider.isDisabled;
    return isDisabled;
  });
  // Determine the display title and thumbnail based on active providers
  const displayTitle = activeProviderForTitle?.title || series?.title || '';
  const displayThumbnail = activeProviderForCover?.thumbnailUrl || series?.thumbnailUrl;
  
  // Calculate effective isActive state based on current provider states
  // A series is considered active if it has at least one enabled, known provider
  const hasActiveProviders = knownProviders.some(provider => {
    const isDisabled = providerDisabledStates[provider.id] ?? provider.isDisabled;
    return !isDisabled;
  });
  
  // Determine the effective series status
  const effectiveStatus = series && (!hasActiveProviders || allProvidersDisabled) ? SeriesStatus.DISABLED : (series?.status ?? SeriesStatus.UNKNOWN);

  // Update series title in context when display title changes
  useEffect(() => {
    // Don't update title if we're deleting
    if (isDeleting) {
      return;
    }
    
    if (displayTitle) {
      setSeriesTitle(displayTitle);
    }
  }, [displayTitle, setSeriesTitle, isDeleting]);

  // Handler for use title changes - enforce exclusivity
  const handleUseTitleChange = async (providerId: string, enabled: boolean) => {
    // Build the complete new state for all providers
    const allUpdates: Record<string, { useTitle?: boolean }> = {};
    
    if (enabled) {
      // Turn off useTitle for all other providers
      Object.keys(providerSwitches).forEach(id => {
        if (id !== providerId) {
          allUpdates[id] = { useTitle: false };
        }
      });
    }
    
    // Set the requested provider's useTitle
    allUpdates[providerId] = { useTitle: enabled };

    // Update state for all affected providers
    setProviderSwitches(prev => {
      const newSwitches = { ...prev };

      Object.keys(allUpdates).forEach(id => {
        if (newSwitches[id]) {
          newSwitches[id] = {
            ...newSwitches[id],
            ...allUpdates[id]
          };
        }
      });

      return newSwitches;
    });

    // Send a single update with all changes using the new values directly
    await updateSeriesWithCompleteState(allUpdates);
  };
  // Handler for use cover changes - enforce exclusivity
  const handleUseCoverChange = async (providerId: string, enabled: boolean) => {
    // Build the complete new state for all providers
    const allUpdates: Record<string, { useCover?: boolean }> = {};
    
    if (enabled) {
      // Turn off useCover for all other providers
      Object.keys(providerSwitches).forEach(id => {
        if (id !== providerId) {
          allUpdates[id] = { useCover: false };
        }
      });
    }
    
    // Set the requested provider's useCover
    allUpdates[providerId] = { useCover: enabled };

    // Update state for all affected providers
    setProviderSwitches(prev => {
      const newSwitches = { ...prev };

      Object.keys(allUpdates).forEach(id => {
        if (newSwitches[id]) {
          newSwitches[id] = {
            ...newSwitches[id],
            ...allUpdates[id]
          };
        }
      });

      return newSwitches;
    });

    // Send a single update with all changes using the new values directly
    await updateSeriesWithCompleteState(allUpdates);
  };  // Handler for use storage changes
  const handleUseStorageChange = async (providerId: string, enabled: boolean) => {
    // Build the update for this provider
    const switchUpdate = { [providerId]: { useStorage: enabled } };

    // Update state
    setProviderSwitches(prev => {
      const newSwitches = { ...prev };
      if (newSwitches[providerId]) {
        newSwitches[providerId] = {
          useTitle: newSwitches[providerId].useTitle,
          useCover: newSwitches[providerId].useCover,
          useStorage: enabled
        };
      }
      return newSwitches;
    });

    // Send update with the new value directly
    await updateSeriesWithCompleteState(switchUpdate);
  };  // Handler for provider disabled state changes
  const handleDisabledChange = (providerId: string, disabled: boolean) => {    setProviderDisabledStates(prev => ({
      ...prev,
      [providerId]: disabled
    }));

    // Trigger immediate update with explicit disabled state
    updateSeriesWithDisabledState(providerId, disabled);
  };  // Handler for provider deletion
  const handleDeleteProvider = (providerId: string) => {
    setProviderDeletedStates(prev => ({
      ...prev,
      [providerId]: true
    }));    // Also set the provider as disabled
    setProviderDisabledStates(prev => ({
      ...prev,
      [providerId]: true
    }));

    // Trigger immediate update with explicit deleted state
    updateSeriesWithDeletedState(providerId, true);  };
  // Handler for fromChapter changes
  const handleFromChapterChange = (providerId: string, value: string) => {
    
    // Create the updated fromChapters object
    const updatedFromChapters = {
      ...providerFromChapters,
      [providerId]: value
    };
    
    // Update state
    setProviderFromChapters(updatedFromChapters);
    
    // We need to modify the updateSeries function to handle explicit fromChapter values
    updateSeriesWithFromChapter(providerId, value);
  };  // Handler for pause downloads toggle
  const handlePausedDownloadsToggle = () => {
    // Calculate new value
    const newPausedDownloadsValue = !pausedDownloads;
    
    // Update state
    setPausedDownloads(newPausedDownloadsValue);
    
    // We need to modify the updateSeries function to handle explicit pausedDownloads value
    updateSeriesWithPausedDownloads(newPausedDownloadsValue);
  };

  // Handler for delete series button click
  const handleDeleteSeriesClick = () => {
    setShowDeleteDialog(true);
  };

  // Handler for delete series confirmation
  const handleDeleteSeriesConfirm = async () => {
    if (!seriesId) return;
    
    try {
      // Set deleting state to prevent further queries and polling
      setIsDeleting(true);
      
      // Cancel all ongoing queries for this series to prevent loops
      await queryClient.cancelQueries({ queryKey: ['series', 'detail', seriesId] });
      await queryClient.cancelQueries({ queryKey: ['downloads', 'series', seriesId] });
      
      await deleteSeries.mutateAsync({ 
        id: seriesId, 
        alsoPhysical: deletePhysicalFiles 
      });
      
      // Navigate back to library after successful deletion
      router.push('/library');
    } catch (error) {
      console.error('Failed to delete series:', error);
      // Reset deleting state on error so user can try again
      setIsDeleting(false);
    } finally {
      setShowDeleteDialog(false);
      setDeletePhysicalFiles(false); // Reset switch for next use
    }
  };

  // Handler for delete series cancellation
  const handleDeleteSeriesCancel = () => {
    setShowDeleteDialog(false);
    setDeletePhysicalFiles(false); // Reset switch for next use
  };

  // Handler for verify integrity button click
  const handleVerifyIntegrityClick = async () => {
    if (!seriesId) return;

    try {
      const result = await verifyIntegrity.mutateAsync(seriesId);
      setVerifyResult(result);

      // Re-fetch series data — verify may have recovered a truncated title
      await queryClient.invalidateQueries({ queryKey: ['series', 'detail', seriesId] });

      if (result.success) {
        // Show success dialog
        setShowVerifyDialog(true);
      } else {
        // Show cleanup dialog with problematic files
        setShowCleanupDialog(true);
      }
    } catch (error) {
      console.error('Failed to verify integrity:', error);
    }
  };

  // Handler for refresh metadata button click
  const handleRefreshClick = async () => {
    if (!seriesId) return;

    try {
      const result = await refreshSeries.mutateAsync(seriesId);
      toast({
        variant: "success",
        title: "Refresh queued",
        description: result.queued > 0
          ? `Checking ${result.queued} source${result.queued === 1 ? '' : 's'} for new metadata & chapters.`
          : "No active sources to refresh.",
      });
    } catch (error) {
      console.error('Failed to refresh series:', error);
      toast({
        variant: "destructive",
        title: "Refresh failed",
        description: "Could not queue the series refresh. Please try again.",
      });
    }
  };

  // Handler for the rename button — opens a confirmation dialog first, since this
  // moves files/folders on disk.
  const handleRenameClick = () => {
    if (!seriesId) return;
    setShowRenameDialog(true);
  };

  // Handler for rename confirmation — renames the series folder + every .cbz to the
  // canonical naming scheme, then reports the outcome.
  const handleRenameConfirm = async () => {
    if (!seriesId) return;
    setShowRenameDialog(false);

    try {
      const result = await renameSeries.mutateAsync(seriesId);

      const parts: string[] = [];
      if (result.folderRenamed) parts.push(`Folder → "${result.newFolder}"`);
      parts.push(`${result.filesRenamed} file${result.filesRenamed === 1 ? '' : 's'} renamed`);
      if (result.filesFailed > 0) parts.push(`${result.filesFailed} failed`);

      const nothingChanged = !result.folderRenamed && result.filesRenamed === 0 && result.filesFailed === 0;

      toast({
        variant: result.filesFailed > 0 ? "destructive" : "success",
        title: nothingChanged ? "Names already correct" : "Rename complete",
        description: nothingChanged
          ? (result.message || "Nothing needed renaming — everything already matches the scheme.")
          : `${parts.join(' · ')}${result.message ? ` · ${result.message}` : ''}`,
      });
    } catch (error) {
      console.error('Failed to rename series:', error);
      toast({
        variant: "destructive",
        title: "Rename failed",
        description: "Could not rename the series files. Please try again.",
      });
    }
  };

  // Handler for verify success dialog close
  const handleVerifyDialogClose = async () => {
    setShowVerifyDialog(false);
    setVerifyResult(null);
    
    // Refresh the series data after successful verification
    if (seriesId) {
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'detail', seriesId] 
      });
    }
  };

  // Handler for cleanup dialog cancel
  const handleCleanupCancel = () => {
    setShowCleanupDialog(false);
    setVerifyResult(null);
  };

  // Handler for cleanup confirmation
  const handleCleanupConfirm = async () => {
    if (!seriesId) return;
    
    try {
      
      await cleanupSeries.mutateAsync(seriesId);
      
      
      // Refresh the series data after cleanup
      await queryClient.invalidateQueries({ 
        queryKey: ['series', 'detail', seriesId] 
      });
      
    } catch (error) {
      console.error('Failed to cleanup series:', error);
    } finally {
      setShowCleanupDialog(false);
      setVerifyResult(null);
    }
  };

  // Helper function to get status display text for archive results
  const getArchiveResultDisplay = (result: ArchiveResult) => {
    switch (result) {
      case ArchiveResult.Fine:
        return { text: 'Fine', color: 'text-green-600' };
      case ArchiveResult.NotAnArchive:
        return { text: 'Not an Archive', color: 'text-red-600' };
      case ArchiveResult.NoImages:
        return { text: 'No Images', color: 'text-yellow-600' };
      case ArchiveResult.NotFound:
        return { text: 'Not Found', color: 'text-red-600' };
      default:
        return { text: 'Unknown', color: 'text-gray-600' };
    }
  };

  // Helper function to update with explicit fromChapter value
  const updateSeriesWithFromChapter = async (providerId: string, fromChapterValue: string) => {
    if (!series) return;
    
    try {
      // Create a temporary modified fromChapters object with the new value
      const effectiveFromChapters = {
        ...providerFromChapters,
        [providerId]: fromChapterValue
      };
      
      // Create updated series object with current state
      const updatedSeries = {
        ...series,
        pausedDownloads: pausedDownloads,
        providers: series.providers.map(provider => {
          const switches = providerSwitches[provider.id];
          // Use the modified fromChapter value for the specific provider
          const currentFromChapterValue = provider.id === providerId ? 
            fromChapterValue : 
            providerFromChapters[provider.id];
          
          const updatedProvider = {
            ...provider,
            isDisabled: provider.isUninstalled ? true : (providerDisabledStates[provider.id] ?? provider.isDisabled),
            isDeleted: providerDeletedStates[provider.id] ?? false,
            fromChapter: currentFromChapterValue !== undefined ? parseFloat(currentFromChapterValue || "0") : provider.fromChapter,
            useTitle: switches?.useTitle ?? provider.useTitle,
            useCover: switches?.useCover ?? provider.useCover,
            isStorage: switches?.useStorage ?? provider.isStorage,
          };
          
          
          return updatedProvider;
        })
      };

      // Call backend update using the mutation hook
      const result = await updateSeriesMutation.mutateAsync(updatedSeries);

      // Update local state with the response from backend
      // This ensures that if the backend changed any values (like fromChapter), the UI reflects them
      if (result && result.providers) {
        const updatedFromChapters: Record<string, string> = {};
        const updatedDisabledStates: Record<string, boolean> = {};
        
        result.providers.forEach(provider => {
          updatedFromChapters[provider.id] = provider.fromChapter?.toString() || "";
          // Update disabled states with backend response
          updatedDisabledStates[provider.id] = provider.isUninstalled ? true : (provider.isDisabled ?? false);
        });
        
        
        // Update the fromChapter state with values from backend response
        setProviderFromChapters(updatedFromChapters);
        
        // Update the disabled states with values from backend response
        setProviderDisabledStates(prev => ({
          ...prev,
          ...updatedDisabledStates
        }));
        
        // Also update pausedDownloads in case it was changed by backend
        setPausedDownloads(result.pausedDownloads ?? false);
      }
      
      // Use optimistic update instead of invalidation
      if (result) {
        queryClient.setQueryData(['series', 'detail', series.id], result);
      }
      
      // Only refetch series data if provider count actually changed
      const hasDeletedProviders = updatedSeries.providers.some((p: any) => p.isDeleted);
      const providerCountChanged = result && result.providers.length !== series.providers.length;
      if (hasDeletedProviders && providerCountChanged) {
        await queryClient.invalidateQueries({ 
          queryKey: ['series', 'detail', series.id] 
        });
      }

    } catch (error) {
      console.error('Failed to update series with fromChapter:', error);
    }
  };

  const updateSeriesWithCompleteState = async (overrideSwitches?: Record<string, { useTitle?: boolean; useCover?: boolean; useStorage?: boolean }>) => {
    if (!series) return;

    try {
      const providersToUpdate = series.providers.map(provider => {
        // Use override switches if provided, otherwise use current state
        const currentSwitches = overrideSwitches?.[provider.id] ? 
          { ...providerSwitches[provider.id], ...overrideSwitches[provider.id] } : 
          providerSwitches[provider.id];
        const currentDisabledState = providerDisabledStates[provider.id];
        const currentFromChapter = providerFromChapters[provider.id];
        
        const updatedProvider = {
          ...provider,
          useTitle: currentSwitches?.useTitle ?? provider.useTitle,
          useCover: currentSwitches?.useCover ?? provider.useCover,
          isStorage: currentSwitches?.useStorage ?? provider.isStorage,
          isDisabled: currentDisabledState ?? provider.isDisabled,
          fromChapter: currentFromChapter !== undefined ? 
            (currentFromChapter === "" ? undefined : parseInt(currentFromChapter)) : 
            provider.fromChapter
        };
        return updatedProvider;
      });

      const updatedSeries = {
        ...series,
        providers: providersToUpdate,
        pausedDownloads: pausedDownloads
      };

      const result = await updateSeriesMutation.mutateAsync(updatedSeries);
      
      if (result && result.providers) {
        
        // Sync local state with backend response
        const newSwitches: Record<string, { useTitle: boolean; useCover: boolean; useStorage: boolean }> = {};
        const newDisabledStates: Record<string, boolean> = {};
        const newFromChapters: Record<string, string> = {};
        
        result.providers.forEach(provider => {
          newSwitches[provider.id] = {
            useTitle: provider.useTitle,
            useCover: provider.useCover,
            useStorage: provider.isStorage
          };
          newDisabledStates[provider.id] = provider.isDisabled;
          newFromChapters[provider.id] = provider.fromChapter?.toString() || "";
        });
        
        setProviderSwitches(newSwitches);
        setProviderDisabledStates(newDisabledStates);
        setProviderFromChapters(newFromChapters);
      } else {
        console.error(`Failed to update series with complete state: no result received`);
      }
    } catch (error) {
      console.error(`Error updating series with complete state:`, error);
    }
  };
  
  // Handle error states first, before accessing series
  if (!seriesId) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-lg text-red-500">Series ID is required</div>
      </div>
    );
  }

  if (isDeleting) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-lg">Deleting series...</div>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="text-lg">Loading series details...</div>
      </div>
    );
  }

  if (error || !series) {
    return (
      <div className="flex flex-col items-center justify-center h-64 gap-4">
        <div className="text-lg text-red-500">
          {error ? "Error loading series" : "Series not found"}
        </div>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => void refetch()}>
            Retry
          </Button>
          <Button variant="ghost" onClick={() => router.push('/library')}>
            Back to Library
          </Button>
        </div>
      </div>
    );
  }
  
  // Derive ExistingSource array from providers for Add Sources mode
  const existingSources: ExistingSource[] = series.providers
    .filter((provider: any) => !providerDeletedStates[provider.id]) // Filter out deleted providers
    .map((provider: any) => ({
      provider: provider.provider,
      scanlator: provider.scanlator,
      mihonProviderId: provider.mihonProviderId,
      lang: provider.lang
    }));

  // Visible (non-deleted) providers array
  const visibleProviders = series.providers.filter(provider => !providerDeletedStates[provider.id]);

  return (<>
    {/* Ribbon — back nav + series title */}
    <SeriesRibbon
      seriesTitle={displayTitle}
      onBack={() => router.push('/library')}
    />

    {/* New layout: full-width hero, then 12-col grid */}
    <div className="w-full">

      {/* Cinematic hero */}
      <SeriesHero
        series={series}
        displayTitle={displayTitle}
        displayThumbnail={displayThumbnail ?? ''}
        effectiveStatus={effectiveStatus}
        pausedDownloads={pausedDownloads}
        canEditSeries={canEdit}
        canDeleteSeries={canDelete}
        canManageDownloads={canManageDownloads}
        verifyPending={verifyIntegrity.isPending}
        refreshPending={refreshSeries.isPending}
        renamePending={renameSeries.isPending}
        onPauseToggle={handlePausedDownloadsToggle}
        onVerify={handleVerifyIntegrityClick}
        onRefresh={handleRefreshClick}
        onRename={handleRenameClick}
        onDelete={handleDeleteSeriesClick}
      />

      {/* Main content — Sources full-width, then aside grid */}
      <div className="mx-auto max-w-7xl px-4 sm:px-6 py-8 sm:py-10 space-y-6 lg:space-y-8">

        {/* Sources — full width, providers tile in 2-col grid at lg+ */}
        <SourcesSection
          series={series}
          providers={visibleProviders}
          existingSources={existingSources}
          providerSwitches={providerSwitches}
          providerDisabledStates={providerDisabledStates}
          providerFromChapters={providerFromChapters}
          providerDeletedStates={providerDeletedStates}
          onUseTitleChange={handleUseTitleChange}
          onUseCoverChange={handleUseCoverChange}
          onUseStorageChange={handleUseStorageChange}
          onFromChapterChange={handleFromChapterChange}
          onEnableDisable={handleDisabledChange}
          onDelete={handleDeleteProvider}
          canEdit={canEdit}
        />

        {/* Chapters — unified, series-level list with per-chapter re-download */}
        <ChaptersSection
          seriesId={series.id}
          paused={pausedDownloads}
          canManage={canManageDownloads}
        />

        {/* Downloads panel */}
        <DownloadsPanel seriesId={series.id} isDeleting={isDeleting} />

      </div>
    </div>

    {/* Delete Series Confirmation Dialog */}
    <Dialog open={showDeleteDialog} onOpenChange={setShowDeleteDialog}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Delete Series</DialogTitle>
          <DialogDescription>
            Are you sure you want to delete "{displayTitle}"? This action cannot be undone.
          </DialogDescription>
        </DialogHeader>
        
        <div className="flex items-center space-x-2 py-4">
          <Switch
            id="delete-physical-files"
            checked={deletePhysicalFiles}
            onCheckedChange={setDeletePhysicalFiles}
          />
          <Label htmlFor="delete-physical-files" className="text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70">
            Also delete Physical Files
          </Label>
        </div>
        
        <DialogFooter className="flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2">
          <Button 
            variant="outline" 
            onClick={handleDeleteSeriesCancel}
            disabled={deleteSeries.isPending}
          >
            Cancel
          </Button>
          <Button 
            variant="destructive" 
            onClick={handleDeleteSeriesConfirm}
            disabled={deleteSeries.isPending}
            className="flex items-center gap-2"
          >
            {deleteSeries.isPending ? (
              <>
                <div className="h-4 w-4 animate-spin rounded-full border-2 border-background border-t-transparent" />
                Deleting...
              </>
            ) : (
              <>
                <Trash2 className="h-4 w-4" />
                Delete
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    {/* Rename (fix folder + .cbz names) Confirmation Dialog */}
    <Dialog open={showRenameDialog} onOpenChange={setShowRenameDialog}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <FolderSync className="h-5 w-5" />
            Rename Files &amp; Folder
          </DialogTitle>
          <DialogDescription>
            This renames the series folder to <span className="font-medium text-foreground">"{displayTitle}"</span> and
            renames every downloaded <span className="font-mono">.cbz</span> to the correct
            <span className="font-mono"> [Source][lang] Title 0000.cbz</span> scheme. Only files downloaded by
            Rensaio are touched. This moves files on disk — make sure no downloads are running for this series.
          </DialogDescription>
        </DialogHeader>

        <DialogFooter className="flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2">
          <Button
            variant="outline"
            onClick={() => setShowRenameDialog(false)}
            disabled={renameSeries.isPending}
          >
            Cancel
          </Button>
          <Button
            onClick={handleRenameConfirm}
            disabled={renameSeries.isPending}
            className="flex items-center gap-2"
          >
            {renameSeries.isPending ? (
              <>
                <div className="h-4 w-4 animate-spin rounded-full border-2 border-background border-t-transparent" />
                Renaming...
              </>
            ) : (
              <>
                <FolderSync className="h-4 w-4" />
                Rename
              </>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    {/* Verify Integrity Success Dialog */}
    {showVerifyDialog && verifyResult?.success && (
      <Dialog open={showVerifyDialog} onOpenChange={setShowVerifyDialog}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-green-600" />
              Integrity Verification Complete
            </DialogTitle>
          </DialogHeader>
          
          <div className="py-4">
            <p className="text-sm text-muted-foreground">
              The integrity verification completed successfully. All files are in good condition.
            </p>
          </div>
          
          <DialogFooter className="flex justify-end gap-2">
            <Button 
              variant="default" 
              onClick={handleVerifyDialogClose}
            >
              OK
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    )}

    {/* Cleanup Confirmation Dialog - shown when verification finds issues */}
    {showCleanupDialog && verifyResult && !verifyResult.success && (
      <Dialog open={showCleanupDialog} onOpenChange={setShowCleanupDialog}>
        <DialogContent className="sm:max-w-lg">
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Trash2 className="h-5 w-5 text-red-600" />
              File Issues Found
            </DialogTitle>
            <DialogDescription>
              The following issues were found with the series files. You can delete these problematic files to clean up the series.
            </DialogDescription>
          </DialogHeader>
          
          <div className="py-4 max-h-60 overflow-y-auto">
            <div className="space-y-2">
              {verifyResult?.badFiles?.map((result: any, index: number) => {
                const displayResult = getArchiveResultDisplay(result.result);
                return (
                  <div key={index} className="flex items-center gap-2 p-2 bg-secondary rounded">
                    <div className={`h-6 w-6 rounded-full flex items-center justify-center bg-red-100`}>
                      <div className="h-2.5 w-2.5 rounded-full bg-red-600" />
                    </div>
                    <div className="text-sm flex-1">
                      <div className="font-medium truncate" title={result.filename}>
                        {result.filename}
                      </div>
                      <div className={`text-xs ${displayResult.color}`}>
                        {displayResult.text}
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          </div>
          
          <DialogFooter className="flex flex-col sm:flex-row sm:justify-end sm:space-x-2">
            <Button 
              variant="outline" 
              onClick={handleCleanupCancel}
            >
              Cancel
            </Button>
            <Button 
              variant="destructive" 
              onClick={handleCleanupConfirm}
              disabled={cleanupSeries.isPending}
              className="flex items-center gap-2"
            >
              <Trash2 className="h-4 w-4" />
              {cleanupSeries.isPending ? "Cleaning..." : "Delete Files"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    )}
  </>  );
}
