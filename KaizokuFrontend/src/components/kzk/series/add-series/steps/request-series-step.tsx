"use client";

import { type AddSeriesState } from "@/components/kzk/series/add-series";
import { Badge } from "@/components/ui/badge";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { type LinkedSeries } from "@/lib/api/types";
import React from "react";
import Image from "next/image";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { useMediaQuery } from "@/hooks/use-media-query";
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";

/**
 * Simplified confirm step for users who can only request (not add) series.
 * Shows a preview of the selected series and an optional note field.
 */
export function RequestSeriesStep({
  formState,
  setCanProgress,
  requestNote,
  onRequestNoteChange,
}: {
  formState: AddSeriesState;
  setCanProgress: React.Dispatch<React.SetStateAction<boolean>>;
  requestNote: string;
  onRequestNoteChange: (note: string) => void;
}) {
  const isDesktop = useMediaQuery("(min-width: 768px)");

  // Get the selected series from the search results
  const selectedSeries = React.useMemo(() => {
    return formState.allLinkedSeries.filter((series: LinkedSeries) =>
      formState.selectedLinkedSeries.includes(series.mihonId ?? series.providerId)
    );
  }, [formState.allLinkedSeries, formState.selectedLinkedSeries]);

  // Always allow progress when we have selected series
  React.useEffect(() => {
    setCanProgress(selectedSeries.length > 0);
  }, [selectedSeries.length, setCanProgress]);

  if (selectedSeries.length === 0) {
    return (
      <div className="mt-4 grid gap-2 rounded-md border bg-secondary p-4 text-center">
        <p className="text-sm text-muted-foreground">No series selected. Go back and select a series to request.</p>
      </div>
    );
  }

  // Use the first selected series as the primary request target
  const primary = selectedSeries[0]!;

  return (
    <div className="mt-4 grid gap-4 rounded-md border bg-secondary p-2 sm:p-4">
      <div className="text-center">
        <p className="text-sm text-muted-foreground">
          Review and submit your request. An admin will review it.
        </p>
      </div>

      {/* Series preview card */}
      <div className="rounded-lg border bg-card p-3 sm:p-4">
        <div className="flex gap-3 sm:gap-4">
          {/* Thumbnail */}
          <div className={`relative flex-shrink-0 ${isDesktop ? 'w-28' : 'w-20'} aspect-[3/4]`}>
            <Image
              src={formatThumbnailUrl(primary.thumbnailUrl)}
              alt={primary.title}
              fill
              sizes="(max-width: 768px) 80px, 112px"
              className="rounded-md object-cover"
            />
            <Badge
              variant="poster"
              className={`absolute max-w-[90%] truncate top-1 left-1 ${isDesktop ? 'text-xs' : 'text-[10px]'}`}
            >
              {primary.provider}
            </Badge>
            <div className={`absolute bottom-1 ${isDesktop ? 'right-2' : 'right-1'}`}>
              <ReactCountryFlag
                countryCode={getCountryCodeForLanguage(primary.lang)}
                svg
                style={{
                  width: isDesktop ? '20px' : '16px',
                  height: isDesktop ? '15px' : '12px',
                  borderColor: "hsl(var(--secondary))",
                  borderWidth: "1px",
                  borderStyle: "solid"
                }}
                title={`${primary.lang.toUpperCase()} (${getCountryCodeForLanguage(primary.lang)})`}
              />
            </div>
          </div>

          {/* Details */}
          <div className="flex-1 min-w-0 space-y-1">
            <h3 className={`font-semibold ${isDesktop ? 'text-lg' : 'text-base'}`}>
              {primary.title}
            </h3>
            <p className="text-sm text-muted-foreground">
              {primary.provider} • {primary.lang.toUpperCase()}
            </p>
          </div>
        </div>

        {/* Additional selected sources */}
        {selectedSeries.length > 1 && (
          <div className="mt-3 pt-3 border-t">
            <p className="text-xs text-muted-foreground mb-1.5">
              {selectedSeries.length} sources selected:
            </p>
            <div className="flex flex-wrap gap-1.5">
              {selectedSeries.map((series) => (
                <Badge key={series.mihonId ?? series.providerId} variant="secondary" className="text-xs">
                  {series.provider} ({series.lang.toUpperCase()})
                </Badge>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Note field */}
      <div className="space-y-1.5">
        <Label htmlFor="request-note" className="text-sm font-medium">
          Note (optional)
        </Label>
        <Textarea
          id="request-note"
          value={requestNote}
          onChange={(e) => onRequestNoteChange(e.target.value)}
          placeholder="Any additional context about this request..."
          rows={2}
          className="bg-card"
        />
      </div>
    </div>
  );
}
