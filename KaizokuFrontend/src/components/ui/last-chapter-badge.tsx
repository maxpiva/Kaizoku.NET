"use client";

import { SeriesStatus } from "@/lib/api/types";
import { getStatusDisplay } from "@/lib/utils/series-status";
import { cn } from "@/lib/utils";

interface LastChapterBadgeProps {
  lastChapter: string | number;
  status?: SeriesStatus;
  className?: string;
}

export function LastChapterBadge({ lastChapter, status, className }: LastChapterBadgeProps) {
  // Get color based on status, fallback to primary if no status provided
  const statusDisplay = status ? getStatusDisplay(status) : { color: "bg-primary" };
  
  return (
    <div className={cn(
      "absolute top-1 right-1 text-white text-xs font-semibold px-2 py-0.5 rounded shadow",
      statusDisplay.color,
      className
    )}>
      {lastChapter}
    </div>
  );
}
