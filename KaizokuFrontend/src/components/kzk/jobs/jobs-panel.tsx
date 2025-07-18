"use client";

import React, { useState, useCallback } from 'react';
import { Button } from '@/components/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Progress } from '@/components/ui/progress';
import { CheckCircle, AlertCircle, Loader2, Download, FileText } from 'lucide-react';
import { useUpdateAllSeries } from '@/lib/api/hooks/useSeries';
import { useSignalRProgress } from '@/lib/api/hooks/useSetupWizard';
import { useImportWizard } from '@/components/providers/import-wizard-provider';
import { JobType } from '@/lib/api/types';

interface JobProgressProps {
  title: string;
  isActive: boolean;
  isCompleted: boolean;
  isFailed: boolean;
  progress: number;
  message?: string;
  errorMessage?: string;
}

function JobProgress({
  title,
  isActive,
  isCompleted,
  isFailed,
  progress,
  message,
  errorMessage
}: JobProgressProps) {
  const getIcon = () => {
    if (isFailed) return <AlertCircle className="h-5 w-5 text-destructive" />;
    if (isCompleted) return <CheckCircle className="h-5 w-5 text-primary" />;
    if (isActive) return <Loader2 className="h-5 w-5 animate-spin text-primary" />;
    return <div className="h-5 w-5 rounded-full border-2 border-muted-foreground/30" />;
  };

  return (
    <Card className={`w-full ${isActive ? 'ring-2 ring-primary' : ''}`}>
      <CardHeader className="pb-3">
        <CardTitle className="flex items-center gap-3 text-base">
          {getIcon()}
          <span>{title}</span>
          {isFailed && (
            <span className="text-sm text-destructive font-normal">Failed</span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="pt-0">
        <div className="space-y-2">
          <Progress value={progress} className="h-2" />
          <div className="flex justify-between text-sm text-muted-foreground">
            <span>{message ?? 'Waiting...'}</span>
            <span>{Math.round(progress)}%</span>
          </div>
          {errorMessage && (
            <div className="text-sm text-destructive bg-destructive/10 p-2 rounded">
              {errorMessage}
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

export function JobsPanel() {
  const [isUpdateAllSeriesRunning, setIsUpdateAllSeriesRunning] = useState(false);
  const updateAllSeriesMutation = useUpdateAllSeries();
  const { startWizard } = useImportWizard();

  const handleUpdateAllSeriesComplete = useCallback(() => {
    setIsUpdateAllSeriesRunning(false);
  }, []);

  const handleUpdateAllSeriesError = useCallback((error: string) => {
    console.error('UpdateAllSeries job failed - callback triggered:', error);
    setIsUpdateAllSeriesRunning(false);
  }, []);

  const { getProgressForJob, isJobCompleted, isJobFailed, getJobProgress } = useSignalRProgress({
    jobTypes: [JobType.UpdateAllSeries],
    onComplete: handleUpdateAllSeriesComplete,
    onError: handleUpdateAllSeriesError,
  });

  const handleImportSeries = () => {
    startWizard();
  };

  const handleUpdateAllSeries = async () => {
    try {
      setIsUpdateAllSeriesRunning(true);
      await updateAllSeriesMutation.mutateAsync();
    } catch (error) {
      console.error('Failed to start UpdateAllSeries:', error);
      setIsUpdateAllSeriesRunning(false);
    }
  };

  // Get progress data for UpdateAllSeries
  const updateAllSeriesProgress = getProgressForJob(JobType.UpdateAllSeries);
  const isUpdateAllSeriesCompleted = isJobCompleted(JobType.UpdateAllSeries);
  const isUpdateAllSeriesFailed = isJobFailed(JobType.UpdateAllSeries);
  const updateAllSeriesProgressValue = isUpdateAllSeriesCompleted ? 100 : getJobProgress(JobType.UpdateAllSeries);

  // Show progress bar ONLY if we have progress data from SignalR (not based on local state)
  // TEMPORARILY: Also show when button is pressed to debug
  const showUpdateAllSeriesProgress = updateAllSeriesProgress !== null || isUpdateAllSeriesRunning;

  return (
    <Card className="h-full flex flex-col">
      <CardHeader className="p-3 pb-0 flex-shrink-0">
        <CardTitle className="text-md">Jobs</CardTitle>
      </CardHeader>
      <CardContent className="flex-1 p-3 space-y-4">
        <div className="space-y-4">
          {/* Import Series Button */}
          <div className="space-y-2">
            <Button 
              size="sm"
              className="gap-1"
              onClick={handleImportSeries}
            >
              <Download className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
                Import Series
              </span>
            </Button>
            <p className="text-sm text-muted-foreground">
              Import Additional Series, or fix existing ones. This is an interactive process, and will open a wizard.
            </p>
          </div>

          {/* Update All Series Button */}
          <div className="space-y-2">
            <Button 
              size="sm"
              className="gap-1"
              onClick={handleUpdateAllSeries}
              disabled={updateAllSeriesMutation.isPending || isUpdateAllSeriesRunning}
            >
              <FileText className="h-4 w-4" />
              <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
                Update All Series
              </span>
            </Button>
            <p className="text-sm text-muted-foreground">
              Applies the selected title to the entire series. This process updates titles for consistency, rewrites the ComicInfo.xml, and sets the series cover to the one you selected.<br/>
⚠️ Note: This may also rename files and convert your series into .cbz archives, but only if the ComicInfo metadata has been updated.
            </p>
          </div>

          {/* Progress Bar - Show ONLY when we receive SignalR messages */}
          {showUpdateAllSeriesProgress && (
            <div className="space-y-2">
              <JobProgress
                title="Updating All Series"
                isActive={!isUpdateAllSeriesCompleted && !isUpdateAllSeriesFailed && updateAllSeriesProgress !== null}
                isCompleted={isUpdateAllSeriesCompleted}
                isFailed={isUpdateAllSeriesFailed}
                progress={updateAllSeriesProgressValue || 0}
                message={updateAllSeriesProgress?.message || 'Processing...'}
                errorMessage={updateAllSeriesProgress?.errorMessage}
              />
            </div>
          )}

          {/* Completion message */}
          {isUpdateAllSeriesCompleted && (
            <div className="bg-secondary border border-green-200 rounded-lg p-4">
              <div className="flex items-center gap-2">
                <CheckCircle className="h-5 w-5 text-primary" />
                <span className="font-medium">
                  Update All Series completed successfully!
                </span>
              </div>
              <p className="text-sm mt-1 text-muted-foreground">
                All series have been updated with consistent naming and metadata.
              </p>
            </div>
          )}
        </div>
      </CardContent>
    </Card>
  );
}
