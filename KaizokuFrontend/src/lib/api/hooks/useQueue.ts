import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { queueService } from '../services/queueService';
import { getProgressHub } from '../signalr/progressHub';
import { JobType, ProgressStatus } from '../types';
import type { ProgressState, DownloadCardInfo } from '../types';

export function useQueue() {
  return useQuery({
    queryKey: ['queue'],
    queryFn: () => queueService.getQueueItems(),
    refetchInterval: 5000, // Refetch every 5 seconds to show progress updates
  });
}

export function useRemoveFromQueue() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (id: string) => queueService.removeFromQueue(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['queue'] });
    },
  });
}

export function useClearQueue() {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: () => queueService.clearQueue(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['queue'] });
    },
  });
}

interface DownloadProgress {
  id: string;
  cardInfo: DownloadCardInfo;
  percentage: number;
  message: string;
  status: ProgressStatus;
  errorMessage?: string;
}

export function useDownloadProgress() {
  const [downloads, setDownloads] = useState<Record<string, DownloadProgress>>({});
  
  useEffect(() => {
    // Only run on client side
    if (typeof window === 'undefined') {
      return;
    }

    let unsubscribe: (() => void) | null = null;
    
    const setupConnection = async () => {
      try {
        await getProgressHub().startConnection();
        
        const progressListener = (progress: ProgressState) => {
          // Only process Download job types
          if (progress.jobType === JobType.Download) {
            setDownloads(prev => {
              const current = prev[progress.id];
              
              // If this is a completed or failed download, remove it from the visual stack
              if (progress.progressStatus === ProgressStatus.Completed || 
                  progress.progressStatus === ProgressStatus.Failed) {
                const { [progress.id]: removed, ...rest } = prev;
                return rest;
              }
              
              // For new downloads or updates, use the download payload
              let cardInfo = current?.cardInfo;
              if (progress.download && !cardInfo) {
                cardInfo = progress.download;
              }
              
              // If we don't have card info yet, skip this update
              if (!cardInfo) {
                return prev;
              }
              
              return {
                ...prev,
                [progress.id]: {
                  id: progress.id,
                  cardInfo,
                  percentage: progress.percentage,
                  message: progress.message,
                  status: progress.progressStatus,
                  errorMessage: progress.errorMessage,
                }
              };
            });
          }
        };
        
        unsubscribe = getProgressHub().onProgress(progressListener);
      } catch (error) {
        console.error('Failed to setup SignalR connection for downloads:', error);
      }
    };

    void setupConnection();

    return () => {
      unsubscribe?.();
    };
  }, []);

  // Convert downloads object to array for easier rendering
  const downloadsList = Object.values(downloads);
  
  return {
    downloads: downloadsList,
    downloadCount: downloadsList.length,
  };
}
