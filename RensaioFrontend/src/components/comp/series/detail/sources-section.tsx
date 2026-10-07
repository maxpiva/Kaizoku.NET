"use client";

import { Plus } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  type ExistingSource,
  type ProviderExtendedInfo,
  type SeriesExtendedInfo,
} from "@/lib/api/types";
import { AddSeries } from "@/components/comp/series/add-series";
import { ProviderCard } from "./provider-card";

export interface ProviderSwitchState {
  useTitle: boolean;
  useCover: boolean;
  useStorage: boolean;
}

export interface SourcesSectionProps {
  series: SeriesExtendedInfo;
  /** Already filtered to non-deleted providers. */
  providers: ProviderExtendedInfo[];
  existingSources: ExistingSource[];
  providerSwitches: Record<string, ProviderSwitchState>;
  providerDisabledStates: Record<string, boolean>;
  providerFromChapters: Record<string, string>;
  providerDeletedStates: Record<string, boolean>;

  // Handlers
  onUseTitleChange: (providerId: string, value: boolean) => void;
  onUseCoverChange: (providerId: string, value: boolean) => void;
  onUseStorageChange: (providerId: string, value: boolean) => void;
  onFromChapterChange: (providerId: string, value: string) => void;
  onEnableDisable: (providerId: string, disabled: boolean) => void;
  onDelete: (providerId: string) => void;

  canEdit: boolean;
}

export function SourcesSection({
  series,
  providers,
  existingSources,
  providerSwitches,
  providerDisabledStates,
  providerFromChapters,
  providerDeletedStates,
  onUseTitleChange,
  onUseCoverChange,
  onUseStorageChange,
  onFromChapterChange,
  onEnableDisable,
  onDelete,
  canEdit,
}: SourcesSectionProps) {
  const permanentCount = providers.filter((provider) =>
    !providerDeletedStates[provider.id] &&
    (providerSwitches[provider.id]?.useStorage ?? provider.isStorage),
  ).length;

  const addSourceTrigger = (
    <Button size="sm" className="h-8 gap-1.5">
      <Plus className="h-3.5 w-3.5" />
      Add Source
    </Button>
  );

  const addSourceEmptyTrigger = (
    <Button variant="outline" size="sm" className="mt-3 gap-1.5">
      <Plus className="h-3.5 w-3.5" /> Add a source
    </Button>
  );

  return (
    <section className="space-y-4">
      <header className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <h2 className="text-lg font-semibold tracking-tight">Sources</h2>
          <span className="inline-flex h-5 min-w-5 items-center justify-center rounded-full bg-foreground/10 px-1.5 text-[11px] font-medium tabular-nums text-muted-foreground">
            {providers.length}
          </span>
        </div>
        {canEdit && (
          <AddSeries
            title={series.title}
            existingSources={existingSources}
            seriesId={series.id}
            existingPermanentCount={permanentCount}
            triggerButton={addSourceTrigger}
          />
        )}
      </header>

      {permanentCount > 1 && (
        <div role="status" className="rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-foreground">
          <strong>{permanentCount} permanent sources:</strong> each downloads its own
          copies of overlapping chapters. Use one permanent source and temporary
          fallbacks to avoid duplicates. Existing copies are not removed when you
          change this setting.
        </div>
      )}

      <div className="space-y-3">
        {providers.map((provider) => {
          const switches =
            providerSwitches[provider.id] ?? {
              useTitle: false,
              useCover: false,
              useStorage: false,
            };
          const isDisabled = provider.isUninstalled
            ? true
            : providerDisabledStates[provider.id] ?? provider.isDisabled;
          const currentFromChapter =
            providerFromChapters[provider.id] ??
            provider.fromChapter?.toString() ??
            "";

          const updatedProvider: ProviderExtendedInfo = {
            ...provider,
            isDisabled,
          };

          return (
            <ProviderCard
              key={provider.id}
              provider={updatedProvider}
              seriesId={series.id}
              useCover={switches.useCover}
              useTitle={switches.useTitle}
              useStorage={switches.useStorage}
              fromChapter={currentFromChapter}
              onUseCoverChange={onUseCoverChange}
              onUseTitleChange={onUseTitleChange}
              onUseStorageChange={onUseStorageChange}
              onDisabledChange={onEnableDisable}
              onDeleteProvider={onDelete}
              onFromChapterChange={onFromChapterChange}
              deletedProviderStates={providerDeletedStates}
              canEdit={canEdit}
              hasOtherPermanentSource={providers.some((other) =>
                other.id !== provider.id && !providerDeletedStates[other.id] && other.isStorage,
              )}
            />
          );
        })}

        {providers.length === 0 && (
          <div className="rounded-xl border border-dashed border-border/60 bg-card/50 p-8 text-center">
            <p className="text-sm text-muted-foreground">
              No sources configured yet.
            </p>
            {canEdit && (
              <AddSeries
                title={series.title}
                existingSources={existingSources}
                seriesId={series.id}
                triggerButton={addSourceEmptyTrigger}
              />
            )}
          </div>
        )}
      </div>
    </section>
  );
}
