"use client";

import { Download, Clock, AlertTriangle } from "lucide-react";
import Link from "next/link";
import { useDownloadsMetrics } from "@/lib/api/hooks/useDownloads";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";

export function DownloadCounters() {
  const { data: metrics, isLoading, error } = useDownloadsMetrics();

  // Don't render anything if there's an error or still loading
  if (isLoading || error || !metrics) {
    return null;
  }

  return (
    <Link 
      href="/queue" 
      className="flex flex-col items-center gap-4 cursor-pointer hover:opacity-80 transition-opacity"
    >
      {/* Active Downloads */}
      <Tooltip>
        <TooltipTrigger asChild>
          <div className="flex flex-col items-center gap-1">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg text-blue-500 md:h-8 md:w-8">
              <Download className="h-6 w-6" />
            </div>
            <span className="text-xs font-medium text-blue-500 min-w-[20px] text-center">
              {metrics.downloads}
            </span>
          </div>
        </TooltipTrigger>
        <TooltipContent side="right">
          Active Downloads: {metrics.downloads}
        </TooltipContent>
      </Tooltip>

      {/* Queued Downloads */}
      <Tooltip>
        <TooltipTrigger asChild>
          <div className="flex flex-col items-center gap-1">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg text-yellow-500 md:h-8 md:w-8">
              <Clock className="h-6 w-6" />
            </div>
            <span className="text-xs font-medium text-yellow-500 min-w-[20px] text-center">
              {metrics.queued}
            </span>
          </div>
        </TooltipTrigger>
        <TooltipContent side="right">
          Queued Downloads: {metrics.queued}
        </TooltipContent>
      </Tooltip>

      {/* Failed Downloads */}
      <Tooltip>
        <TooltipTrigger asChild>
          <div className="flex flex-col items-center gap-1">
            <div className="flex h-9 w-9 items-center justify-center rounded-lg text-red-500 md:h-8 md:w-8">
              <AlertTriangle className="h-6 w-6" />
            </div>
            <span className="text-xs font-medium text-red-500 min-w-[20px] text-center">
              {metrics.failed}
            </span>
          </div>
        </TooltipTrigger>
        <TooltipContent side="right">
          Failed Downloads: {metrics.failed}
        </TooltipContent>
      </Tooltip>
    </Link>
  );
}
