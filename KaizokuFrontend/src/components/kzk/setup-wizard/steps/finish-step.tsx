"use client";

import React, { useState, useEffect, useRef } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Progress } from "@/components/ui/progress";
import { CheckCircle, AlertCircle, Loader2, Flag } from "lucide-react";
import { useSetupWizardImports, useSetupWizardImportSeriesWithOptions, useSignalRProgress } from "@/lib/api/hooks/useSetupWizard";
import { JobType, ImportStatus } from "@/lib/api/types";

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

interface FinishStepProps {
  setError: (error: string | null) => void;
  setIsLoading: (loading: boolean) => void;
  setCanProgress: (canProgress: boolean) => void;
  disableDownloads?: boolean;
}

export function FinishStep({ setError, setIsLoading, setCanProgress, disableDownloads = false }: FinishStepProps) {
  const hasTriggeredImportRef = useRef(false);
  const [importCompleted, setImportCompleted] = useState(false);
  const { hasScrollbar, containerRef } = useScrollbarDetection();
  const { data: imports } = useSetupWizardImports();
  const importMutation = useSetupWizardImportSeriesWithOptions();
  const { getProgressForJob, isJobCompleted, isJobFailed, getJobProgress } = useSignalRProgress({
    jobTypes: [JobType.ImportSeries],
    onComplete: (jobType) => {
      if (jobType === JobType.ImportSeries) {
        setImportCompleted(true);
      }
    },
    onError: (error, jobType) => {
      console.error(`Import failed: ${jobType}`, error);
      setError(`Import failed: ${error}`);
    },
  });

  // Single effect to handle import triggering
  useEffect(() => {
    const alreadyCompleted = isJobCompleted(JobType.ImportSeries);
    const alreadyFailed = isJobFailed(JobType.ImportSeries);
    const progress = getJobProgress(JobType.ImportSeries);
    const alreadyRunning = progress > 0 && !alreadyCompleted && !alreadyFailed;
    
    // Only trigger if we haven't already triggered and job isn't running/completed/failed
    if (!hasTriggeredImportRef.current && !alreadyCompleted && !alreadyRunning && !alreadyFailed) {
      hasTriggeredImportRef.current = true;
      setError(null);
      
      // Determine what to import
      if (imports && imports.length > 0) {
        const importsToProcess = imports.filter(item =>
          item.status === ImportStatus.Import || item.status === ImportStatus.DoNotChange
        );
          if (importsToProcess.length > 0) {
           importMutation.mutateAsync(disableDownloads).catch((error: any) => {
            console.error('Import failed:', error);
            setError('Failed to start import process');
            hasTriggeredImportRef.current = false; // Reset on error to allow retry
          });
        } else {
          setImportCompleted(true);
        }
      } else {
        importMutation.mutateAsync(disableDownloads).catch((error: any) => {
          console.error('Import failed:', error);
          setError('Failed to start import process');
          hasTriggeredImportRef.current = false; // Reset on error to allow retry
        });
      }
    }
  }, [imports, importMutation, setError, isJobCompleted, isJobFailed, getJobProgress]);

  // Update loading and progress states
  useEffect(() => {
    const isImporting = hasTriggeredImportRef.current && !importCompleted && !isJobCompleted(JobType.ImportSeries) && !isJobFailed(JobType.ImportSeries);
    setIsLoading(isImporting);
    setCanProgress(importCompleted || isJobCompleted(JobType.ImportSeries));
  }, [importCompleted, isJobCompleted, isJobFailed, setIsLoading, setCanProgress]);

  const progressData = getProgressForJob(JobType.ImportSeries);
  const progress = importCompleted || isJobCompleted(JobType.ImportSeries) ? 100 : getJobProgress(JobType.ImportSeries);
  const isFailed = isJobFailed(JobType.ImportSeries);
  const isActive = hasTriggeredImportRef.current && !importCompleted && !isJobCompleted(JobType.ImportSeries) && !isFailed;

  const getIcon = () => {
    if (isFailed) return <AlertCircle className="h-6 w-6 text-destructive" />;
    if (importCompleted || isJobCompleted(JobType.ImportSeries)) return <CheckCircle className="h-6 w-6 text-primary" />;
    if (isActive) return <Loader2 className="h-6 w-6 animate-spin text-primary" />;
    return <Flag className="h-6 w-6 text-muted-foreground" />;
  };

  const getStatusMessage = () => {
    if (isFailed) return "Import process failed";
    if (importCompleted || isJobCompleted(JobType.ImportSeries)) return "Import process completed successfully!";
    if (isActive) return "Importing series...";
    return "Preparing to import series...";
  };

  const getDetailMessage = () => {
    if (importCompleted || isJobCompleted(JobType.ImportSeries)) {
      return "All selected series have been imported into your library. The setup wizard is now complete!";
    }
    if (isActive && progressData?.message) {
      return progressData.message;
    }
    if (isFailed && progressData?.errorMessage) {
      return progressData.errorMessage;
    }
    return "This may take a few minutes depending on the number of series being imported.";
  };
  return (
    <div className="space-y-6">
      <div className="text-sm text-muted-foreground">
        Final step: Importing your selected series into the library. 
        Please wait while the process completes.
      </div>

      <div 
        ref={containerRef}
        className={`max-h-[60vh] p-0.5 overflow-y-auto ${hasScrollbar ? 'pr-2' : ''}`}
      >
        <div className="space-y-6">
          <Card className={`w-full ${isActive ? 'ring-2 ring-primary' : ''}`}>
            <CardHeader>
              <CardTitle className="flex items-center gap-3">
                {getIcon()}
                <span>Series Import</span>
              </CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="space-y-2">
                <Progress value={progress} className="h-3" />
                <div className="flex justify-between text-sm">
                  <span className="text-muted-foreground">{getStatusMessage()}</span>
                  <span className="text-muted-foreground">{Math.round(progress)}%</span>
                </div>
              </div>
              
              <div className="text-sm text-muted-foreground">
                {getDetailMessage()}
              </div>

              {progressData?.errorMessage && (
                <div className="text-sm text-destructive bg-destructive/10 p-3 rounded">
                  <strong>Error:</strong> {progressData.errorMessage}
                </div>
              )}
            </CardContent>
          </Card>

          {(importCompleted || isJobCompleted(JobType.ImportSeries)) && (
            <div className="bg-secondary border border-green-200 rounded-lg p-4">
              <div className="flex items-center gap-2">
                <CheckCircle className="h-5 w-5 text-primary" />
                <span className="text font-medium">
                  Import process completed successfully!
                </span>
              </div>
              <p className="text mb-4">
                Congratulations! Your Kaizoku.NET import wizard is now complete. 
              </p>
            </div>
          )}

          {isFailed && (
            <div className="bg-red-50 border border-red-200 rounded-lg p-6 text-center">
              <AlertCircle className="h-12 w-12 text-red-500 mx-auto mb-4" />
              <h3 className="text-lg font-semibold text-red-800 mb-2">
                Import Failed
              </h3>
              <p className="text-red-700 mb-4">
                The import process encountered an error. You can try again or skip this step and manually import series later.
              </p>
              <div className="text-sm text-red-600">
                Check the error details above and try again, or contact support if the issue persists.
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
