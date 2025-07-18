"use client";

import React, { useState, useEffect } from 'react';
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Loader2, Download, Calendar, Library, Plug } from "lucide-react";
import { useSetupWizardImportTotals } from "@/lib/api/hooks/useSetupWizard";
import type { ImportTotals } from "@/lib/api/types";

interface ScheduleUpdatesStepProps {
  setError: React.Dispatch<React.SetStateAction<string | null>>;
  setIsLoading: React.Dispatch<React.SetStateAction<boolean>>;
  setCanProgress: React.Dispatch<React.SetStateAction<boolean>>;
  onDownloadOptionChange?: (disableDownloads: boolean) => void;
}

export function ScheduleUpdatesStep({
  setError,
  setIsLoading,
  setCanProgress,
  onDownloadOptionChange,
}: ScheduleUpdatesStepProps) {
  const [downloadOption, setDownloadOption] = useState<string>("proceed");
  const [importTotals, setImportTotals] = useState<ImportTotals | null>(null);
  
  const {
    data: totalsData,
    error: totalsError,
    isLoading: totalsLoading,
    refetch: refetchTotals,
  } = useSetupWizardImportTotals();

  // Load import totals when component mounts
  useEffect(() => {
    void refetchTotals();
  }, [refetchTotals]);

  // Handle totals data
  useEffect(() => {
    if (totalsData) {
      setImportTotals(totalsData);
      setError(null);
    }
  }, [totalsData, setError]);

  // Handle loading state
  useEffect(() => {
    setIsLoading(totalsLoading);
  }, [totalsLoading, setIsLoading]);

  // Handle errors
  useEffect(() => {
    if (totalsError) {
      setError(`Failed to load import totals: ${totalsError.message}`);
    } else {
      setError(null);
    }
  }, [totalsError, setError]);

  // Handle progress state - always allow progress since user can choose either option
  useEffect(() => {
    setCanProgress(true);
  }, [setCanProgress]);

  // Handle download option changes
  useEffect(() => {
    const disableDownloads = downloadOption === "disable";
    onDownloadOptionChange?.(disableDownloads);
  }, [downloadOption, onDownloadOptionChange]);

  const handleDownloadOptionChange = (value: string) => {
    setDownloadOption(value);
  };

  if (totalsLoading) {
    return (
      <div className="flex items-center justify-center p-8">
        <div className="flex items-center gap-2">
          <Loader2 className="h-6 w-6 animate-spin" />
          <span>Loading import totals...</span>
        </div>
      </div>
    );
  }

  if (!importTotals) {
    return (
      <div className="text-center p-8">
        <p className="text-muted-foreground">No import data available</p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="text-center">
        <h3 className="text-lg font-semibold mb-2">Schedule Updates</h3>
        <p className="text-sm text-muted-foreground">
          Review the incoming schedule, and choose between hell let loose now, or do in steps.
        </p>
      </div>

      {/* Import Totals Summary */}
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Calendar className="h-5 w-5" />
            Import Summary
          </CardTitle>
        </CardHeader>
        <CardContent>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">             <div className="flex items-center gap-3 p-4 rounded-lg bg-primary/10">
              <Library className="h-8 w-8 text-primary" />
              <div>
                <div className="text-2xl font-bold">{importTotals.totalSeries}</div>
                <div className="text-sm text-muted-foreground">Series</div>
              </div>
            </div>
            <div className="flex items-center gap-3 p-4 rounded-lg bg-secondary/10">
              <Plug className="h-8 w-8 text-secondary-foreground" />
              <div>
                <div className="text-2xl font-bold">{importTotals.totalProviders}</div>
                <div className="text-sm text-muted-foreground">Sources</div>
              </div>
            </div>
            <div className="flex items-center gap-3 p-4 rounded-lg bg-primary/10">
              <Download className="h-8 w-8 text-blue-600 dark:text-blue-400" />
              <div>
                <div className="text-2xl font-bold">
                  {importTotals.totalDownloads}
                </div>
                <div className="text-sm text-muted-foreground">Scheduled Downloads</div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>

      {/* Download Options */}
      <Card>
        <CardHeader>
          <CardTitle>Download Schedule Options</CardTitle>
        </CardHeader>
        <CardContent>
          <RadioGroup
            value={downloadOption}
            onValueChange={handleDownloadOptionChange}
            className="space-y-4"
          >
            <div className="flex items-start space-x-3 p-4 border rounded-lg hover:bg-muted/50 transition-colors">
              <RadioGroupItem value="proceed" id="proceed" className="mt-1" />
              <div className="space-y-1 flex-1">
                <Label 
                  htmlFor="proceed" 
                  className="text-base font-medium cursor-pointer"
                >
                  Proceed with scheduled downloads
                </Label>
                <p className="text-sm text-muted-foreground">
                  All {importTotals.totalDownloads} pending downloads will be automatically scheduled and start downloading according to your configured settings.
                </p>
              </div>
            </div>
            
            <div className="flex items-start space-x-3 p-4 border rounded-lg hover:bg-muted/50 transition-colors">
              <RadioGroupItem value="disable" id="disable" className="mt-1" />
              <div className="space-y-1 flex-1">
                <Label 
                  htmlFor="disable" 
                  className="text-base font-medium cursor-pointer"
                >
                  Start with downloads disabled
                </Label>
                <p className="text-sm text-muted-foreground">
                  All series sources will be imported but disabled. You can manually enable them one by one from the series management page when you're ready.
                </p>
              </div>
            </div>
          </RadioGroup>
        </CardContent>
      </Card>

      {downloadOption === "disable" && (
        <div className="p-4 border border-yellow-200 bg-yellow-50 dark:border-yellow-800 dark:bg-yellow-900/20 rounded-lg">
          <div className="flex items-start gap-2">
            <div className="text-yellow-600 dark:text-yellow-400 mt-0.5">
              <svg className="h-5 w-5" fill="currentColor" viewBox="0 0 20 20">
                <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
              </svg>
            </div>
            <div className="flex-1">
              <p className="text-sm font-medium text-yellow-800 dark:text-yellow-200">
                Note: Downloads will be disabled
              </p>
              <p className="text-xs text-yellow-700 dark:text-yellow-300 mt-1">
                You'll need to manually enable sources for each series in the series management page to start downloading new chapters.
              </p>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
