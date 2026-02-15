import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState, useRef } from 'react';
import { setupWizardService } from '../services/setupWizardService';
import { getProgressHub } from '../signalr/progressHub';
import type { ImportInfo, ProgressState, JobType, LinkedSeries, ImportTotals } from '../types';
import { ProgressStatus } from '../types';

export function useSetupWizardScanLocalFiles() {
  return useMutation({
    mutationFn: () => setupWizardService.scanLocalFiles(),
    onError: (error) => {
      console.error('Scan local files error:', error);
    },
  });
}

export function useSetupWizardInstallExtensions() {
  return useMutation({
    mutationFn: () => setupWizardService.installAdditionalExtensions(),
    onError: (error) => {
      console.error('Install extensions error:', error);
    },
  });
}

export function useSetupWizardSearchSeries() {
  return useMutation({
    mutationFn: () => setupWizardService.searchSeries(),
    onError: (error) => {
      console.error('Search series error:', error);
    },
  });
}

export function useSetupWizardImports() {
  return useQuery({
    queryKey: ['setup-wizard', 'imports'],
    queryFn: () => setupWizardService.getImports(),
    enabled: false, // Only fetch when explicitly requested
  });
}

export function useSetupWizardImportSeries() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: () => setupWizardService.importSeries(),
    onSuccess: () => {
      // Invalidate imports query after successful import
      void queryClient.invalidateQueries({ queryKey: ['setup-wizard', 'imports'] });
    },
    onError: (error) => {
      console.error('Import series error:', error);
    },
  });
}

export function useSetupWizardImportTotals() {
  return useQuery({
    queryKey: ['setup-wizard', 'import-totals'],
    queryFn: () => setupWizardService.getImportTotals(),
    enabled: false, // Only fetch when explicitly requested
  });
}

export function useSetupWizardImportSeriesWithOptions() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (disableDownloads: boolean) => setupWizardService.importSeriesWithOptions(disableDownloads),
    onSuccess: () => {
      // Invalidate imports query after successful import
      void queryClient.invalidateQueries({ queryKey: ['setup-wizard', 'imports'] });
    },
    onError: (error) => {
      console.error('Import series with options error:', error);
    },
  });
}

export function useSetupWizardAugmentSeries() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: ({ path, linkedSeries }: { path: string; linkedSeries: LinkedSeries[] }) => 
      setupWizardService.augmentSeries(path, linkedSeries),
    onSuccess: () => {
      // Invalidate imports query after successful augmentation
      void queryClient.invalidateQueries({ queryKey: ['setup-wizard', 'imports'] });
    },
    onError: (error) => {
      console.error('Augment series error:', error);
    },
  });
}

export function useSetupWizardUpdateImport() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (importInfo: ImportInfo) => setupWizardService.updateImport(importInfo),
    onError: (error) => {
      console.error('Update import error:', error);
    },
  });
}

export function useSetupWizardLookupSeries() {
  return useMutation({
    mutationFn: ({ keyword, searchSources }: { keyword: string; searchSources?: string[] }) => 
      setupWizardService.lookupSeries(keyword, searchSources),
    onError: (error) => {
      console.error('Lookup series error:', error);
    },
  });
}

interface ProgressTrackingOptions {
  jobTypes: JobType[];
  onProgress?: (progress: ProgressState) => void;
  onComplete?: (jobType: JobType) => void;
  onError?: (error: string, jobType: JobType) => void;
}

export function useSignalRProgress(options: ProgressTrackingOptions) {
  const { jobTypes, onProgress, onComplete, onError } = options;
  
  // Create stable refs for the callbacks to prevent connection recreation
  const onProgressRef = useRef(onProgress);
  const onCompleteRef = useRef(onComplete);
  const onErrorRef = useRef(onError);
  
  // Update refs when callbacks change
  useEffect(() => {
    onProgressRef.current = onProgress;
    onCompleteRef.current = onComplete;
    onErrorRef.current = onError;
  }, [onProgress, onComplete, onError]);
  
  const [progressStates, setProgressStates] = useState<Record<JobType, ProgressState | null>>(() => {
    const initial = {} as Record<JobType, ProgressState | null>;
    jobTypes.forEach(jobType => {
      initial[jobType] = null;
    });
    return initial;
  });
    useEffect(() => {
    // Only run on client side
    if (typeof window === 'undefined') {
      return;
    }

    let unsubscribe: (() => void) | null = null;
    
    const setupConnection = async () => {
      try {
        await getProgressHub().startConnection();
        
        // Create a stable listener function that doesn't change
        const progressListener = (progress: ProgressState) => {
          if (jobTypes.includes(progress.jobType)) {
            setProgressStates(prev => ({
              ...prev,
              [progress.jobType]: progress,
            }));

            onProgressRef.current?.(progress);

            if (progress.progressStatus === ProgressStatus.Completed) {
              onCompleteRef.current?.(progress.jobType);
            } else if (progress.progressStatus === ProgressStatus.Failed) {
              onErrorRef.current?.(progress.errorMessage ?? 'Unknown error', progress.jobType);
            }
          }
        };
        
        unsubscribe = getProgressHub().onProgress(progressListener);
      } catch (error) {
        console.error('Failed to setup SignalR connection:', error);
      }
    };

    void setupConnection();

    return () => {
      unsubscribe?.();
    };
  }, [jobTypes]);

  const getProgressForJob = (jobType: JobType): ProgressState | null => {
    return progressStates[jobType];
  };

  const isJobCompleted = (jobType: JobType): boolean => {
    const progress = progressStates[jobType];
    return progress?.progressStatus === ProgressStatus.Completed;
  };

  const isJobFailed = (jobType: JobType): boolean => {
    const progress = progressStates[jobType];
    return progress?.progressStatus === ProgressStatus.Failed;
  };
  const getJobProgress = (jobType: JobType): number => {
    const progress = progressStates[jobType];
    return progress?.percentage ?? 0;
  };

  return {
    progressStates,
    getProgressForJob,
    isJobCompleted,
    isJobFailed,
    getJobProgress,
  };
}
