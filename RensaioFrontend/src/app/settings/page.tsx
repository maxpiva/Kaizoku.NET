"use client";

import React, { useState } from 'react';
import { SettingsManager } from "@/components/comp/settings-manager";
import { Separator } from "@/components/ui/separator";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { RefreshCw, ExternalLink } from 'lucide-react';
import { useScrobblerUnmatched, useAutoMatchAll, useTriggerSync } from '@/lib/api/hooks/useScrobbler';
import { ScrobblerProvider } from '@/lib/api/types';
import { SeriesMatchDialog } from '@/components/comp/scrobbler/series-match-dialog';

export default function SettingsPage() {
  const { data: unmatched } = useScrobblerUnmatched();
  const autoMatchAll = useAutoMatchAll();
  const triggerSync = useTriggerSync();
  const [selectedSeries, setSelectedSeries] = useState<{ seriesId: string; provider: ScrobblerProvider } | null>(null);

  const unmatchedCount = unmatched?.filter(u => u.mappingStatus === 0).length ?? 0;

  return (
    <div className="space-y-8">
      <SettingsManager
        showHeader={true}
        showSaveButton={true}
        title="Settings"
        description="Configure your Rensaiō application settings"
      />

      <Separator />

      {/* Unmatched Series Section */}
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="text-lg font-semibold">Unmatched Series</h3>
            <p className="text-sm text-muted-foreground">
              {unmatchedCount > 0
                ? `${unmatchedCount} series need manual matching`
                : 'All series are matched'}
            </p>
          </div>
          {unmatchedCount > 0 && (
            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  // Auto-match across all providers
                  Object.values(ScrobblerProvider).filter(v => typeof v === 'number').forEach(p => {
                    autoMatchAll.mutate(p as ScrobblerProvider);
                  });
                }}
                disabled={autoMatchAll.isPending}
              >
                <RefreshCw className={`h-4 w-4 mr-2 ${autoMatchAll.isPending ? 'animate-spin' : ''}`} />
                Auto-Match All
              </Button>
            </div>
          )}
        </div>

        {unmatched && unmatched.length > 0 && (
          <div className="rounded-md border">
            <div className="p-4">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b text-left">
                    <th className="pb-2 font-medium">Series</th>
                    <th className="pb-2 font-medium">Provider</th>
                    <th className="pb-2 font-medium">Status</th>
                    <th className="pb-2 font-medium">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {unmatched.filter(s => s.mappingStatus !== 2).slice(0, 20).map((status) => (
                    <tr key={`${status.seriesId}-${status.provider}`} className="border-b last:border-0">
                      <td className="py-2">{status.seriesTitle}</td>
                      <td className="py-2">{ScrobblerProvider[status.provider]}</td>
                      <td className="py-2">
                        {status.mappingStatus === 0 && (
                          <Badge variant="secondary">Not matched</Badge>
                        )}
                        {status.mappingStatus === 1 && (
                          <Badge variant="default">Auto-matched ({Math.round((status.matchScore ?? 0) * 100)}%)</Badge>
                        )}
                        {status.mappingStatus === 3 && (
                          <Badge variant="secondary">Disabled</Badge>
                        )}
                      </td>
                      <td className="py-2">
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setSelectedSeries({ seriesId: status.seriesId, provider: status.provider })}
                        >
                          <ExternalLink className="h-4 w-4 mr-1" />
                          Match
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </div>

      {/* Series Match Dialog */}
      {selectedSeries && (
        <SeriesMatchDialog
          seriesId={selectedSeries.seriesId}
          provider={selectedSeries.provider}
          open={true}
          onOpenChange={(open: boolean) => {
            if (!open) setSelectedSeries(null);
          }}
        />
      )}
    </div>
  );
}
