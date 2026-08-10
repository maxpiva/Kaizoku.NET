"use client";
import { type AddSeriesState } from "@/components/comp/series/add-series";
import { SearchSeriesStep } from "@/components/comp/series/add-series/steps/search-series-step";
import { ConfirmSeriesStep } from "@/components/comp/series/add-series/steps/confirm-series-step";
import {
  AlertTriangle,
  Check,
  LoaderCircle,
  Plus,
  X,
} from "lucide-react";
import React from "react";
import { useAddSeries } from "@/lib/api/hooks/useSeries";
import { useAugmentSeries } from "@/lib/api/hooks/useSearch";
import { providerService } from "@/lib/api/services/providerService";
import { useToast } from "@/hooks/use-toast";
import { useQueryClient } from "@tanstack/react-query";
import { type LinkedSeries, type FullSeries, type ExistingSource, type AugmentedResponse } from "@/lib/api/types";

/** Find the first series whose language matches the user's preferred languages. */
function findPreferredSeriesIndex(series: FullSeries[], preferredLanguages: string[] | undefined | null): number {
  if (!Array.isArray(series) || series.length === 0) return 0;
  if (!Array.isArray(preferredLanguages) || preferredLanguages.length === 0) return 0;
  for (const preferredLang of preferredLanguages) {
    if (typeof preferredLang === 'string') {
      const matchingIndex = series.findIndex(s =>
        typeof s.lang === 'string' &&
        s.lang.toLowerCase() === preferredLang.toLowerCase()
      );
      if (matchingIndex !== -1) return matchingIndex;
    }
  }
  return 0;
}

export interface AddSeriesStepsProps {
  onFinish: () => void;
  title?: string;
  existingSources?: ExistingSource[];
  seriesId?: string;
  isAddSourcesMode?: boolean;
  onOpenChange?: (open: boolean) => void;
}

export function AddSeriesSteps({
  onFinish,
  title,
  existingSources,
  seriesId,
  isAddSourcesMode = false,
  onOpenChange,
}: AddSeriesStepsProps) {
  const [formState, setFormState] = React.useState<AddSeriesState>({
    selectedLinkedSeries: [],
    searchKeyword: title || "",
    allLinkedSeries: [],
    discoveryLinkedSeries: [],
    fullSeries: [],
    originalAugmentedResponse: undefined,
    storagePath: undefined,
  });
  const [error, setError] = React.useState<string | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [canProgress, setCanProgress] = React.useState(false);
  const [pendingNextStep, setPendingNextStep] = React.useState(false);
  const [currentStep, setCurrentStep] = React.useState(0);
  const [isInstallingExtensions, setIsInstallingExtensions] = React.useState(false);

  const addSeries = useAddSeries();
  const augmentSeries = useAugmentSeries();
  const queryClient = useQueryClient();
  const { toast } = useToast();

  React.useEffect(() => {
    if (pendingNextStep && formState.fullSeries.length > 0) {
      setPendingNextStep(false);
      setCurrentStep((step) => step + 1);
    }
  }, [pendingNextStep, formState.fullSeries]);

  const handleSubmit = async () => {
    try {
      const selectedSeries = formState.fullSeries.filter((series: FullSeries) => series.isSelected);

      if (!formState.originalAugmentedResponse) {
        throw new Error('Original augmented response not found');
      }
      const finalAugmentedResponse: AugmentedResponse = {
        ...formState.originalAugmentedResponse,
        series: selectedSeries,
        storageFolderPath: formState.storagePath || formState.originalAugmentedResponse.storageFolderPath,
        existingSeries: isAddSourcesMode || formState.originalAugmentedResponse.existingSeries,
        existingSeriesId: (isAddSourcesMode && seriesId) ? seriesId : formState.originalAugmentedResponse.existingSeriesId,
      };

      await addSeries.mutateAsync(finalAugmentedResponse);

      if (isAddSourcesMode && seriesId) {
        await queryClient.invalidateQueries({ queryKey: ['series', 'detail', seriesId] });
      } else {
        await queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
      }

      onOpenChange?.(false);
      onFinish();
    } catch (err) {
      console.error('Failed to add series:', err);
      const msg = err instanceof Error ? err.message : 'Failed to add series.';
      setError(msg);
      toast({ title: 'Failed to add series', description: msg, variant: 'destructive' });
    }
  };

  /**
   * Installs the extensions backing any selected discovery ("not installed") results
   * via the normal install flow, so the standard augment/add pipeline can proceed.
   * Returns false (and surfaces the error) if any install fails.
   */
  const installMissingExtensions = async (selectedLinked: LinkedSeries[]): Promise<boolean> => {
    const notInstalled = selectedLinked.filter(s => s.installed === false && s.extensionPkg);
    if (notInstalled.length === 0) return true;

    // Dedupe by package; keep the repo name for a deterministic install.
    const packages = new Map<string, string | undefined>();
    notInstalled.forEach(s => {
      if (!packages.has(s.extensionPkg!)) packages.set(s.extensionPkg!, s.extensionRepoName ?? undefined);
    });

    setIsInstallingExtensions(true);
    try {
      for (const [pkg, repoName] of packages) {
        const label = notInstalled.find(s => s.extensionPkg === pkg)?.extensionName ?? pkg;
        toast({ title: "Installing extension", description: `Installing ${label}…` });
        await providerService.installProvider(pkg, repoName ? { repoName } : undefined);
      }
      // Mark the affected results as installed so a retry/back-navigation doesn't reinstall.
      const installedPkgs = new Set(packages.keys());
      setFormState(prev => ({
        ...prev,
        allLinkedSeries: prev.allLinkedSeries.map(s =>
          s.extensionPkg && installedPkgs.has(s.extensionPkg) ? { ...s, installed: true } : s
        ),
        discoveryLinkedSeries: prev.discoveryLinkedSeries.map(s =>
          s.extensionPkg && installedPkgs.has(s.extensionPkg) ? { ...s, installed: true } : s
        ),
      }));
      return true;
    } catch (err) {
      console.error('Failed to install extension:', err);
      const msg = err instanceof Error ? err.message : 'Failed to install extension.';
      setError(msg);
      toast({ title: 'Failed to install extension', description: msg, variant: 'destructive' });
      return false;
    } finally {
      setIsInstallingExtensions(false);
    }
  };

  const handleNext = async () => {
    try {
      const allLinkedSeries = formState.allLinkedSeries;
      const selectedLinked: LinkedSeries[] = allLinkedSeries.filter((series: LinkedSeries) =>
        formState.selectedLinkedSeries.includes(series.mihonId ?? series.providerId)
      );
      // Discovery results first need their extension installed via the normal install flow.
      const installed = await installMissingExtensions(selectedLinked);
      if (!installed) return;
      const augmentedResponse = await augmentSeries.mutateAsync(selectedLinked);

      if (isAddSourcesMode && seriesId) {
        augmentedResponse.existingSeries = true;
        augmentedResponse.existingSeriesId = seriesId;
      }

      const preferredIndex = isAddSourcesMode ? 0 : findPreferredSeriesIndex(augmentedResponse.series, augmentedResponse.preferredLanguages);
      const fullSeriesWithDefaults: FullSeries[] = augmentedResponse.series.map((series: FullSeries, index: number): FullSeries => ({
        ...series,
        isStorage: isAddSourcesMode ? false : index === preferredIndex,
        useCover: isAddSourcesMode ? false : index === preferredIndex,
        useTitle: isAddSourcesMode ? false : index === preferredIndex,
      }));

      setFormState((prev: AddSeriesState): AddSeriesState => ({
        ...prev,
        fullSeries: fullSeriesWithDefaults,
        originalAugmentedResponse: augmentedResponse,
        storagePath: augmentedResponse.storageFolderPath,
      }));
      setPendingNextStep(true);
    } catch (err) {
      console.error('Failed to augment series:', err);
    }
  };

  const handlePrev = () => {
    setCurrentStep((step) => Math.max(0, step - 1));
  };

  const isPending = Boolean(augmentSeries.isPending) || Boolean(addSeries.isPending) || isInstallingExtensions;
  const isStage0 = currentStep === 0;

  const getButtonLabel = (): { label: string; icon: React.ReactNode } => {
    if (!isStage0) {
      if (isAddSourcesMode) return { label: "Add Sources", icon: isPending ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <Plus className="h-3.5 w-3.5" /> };
      return { label: "Add Series", icon: isPending ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <Plus className="h-3.5 w-3.5" /> };
    }
    return { label: "Next", icon: isPending ? <LoaderCircle className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" /> };
  };

  const getStageLabel = (): string => {
    if (isStage0) return "STAGE 01 / SEARCH";
    return "STAGE 02 / CONFIRM";
  };

  const getLeftMetaCopy = (): string => {
    if (isStage0) {
      return `${formState.allLinkedSeries.length} results · ${formState.selectedLinkedSeries.length} selected`;
    }
    const validSelected = formState.fullSeries.filter((s: FullSeries) => s.isSelected);
    const totalChapters = validSelected.reduce((sum: number, s: FullSeries) => sum + (s.chapterCount ?? 0), 0);
    return `${validSelected.length} sources · ${totalChapters} chapters`;
  };

  const { label, icon } = getButtonLabel();

  return (
    <div className="cmd-card">
      <button
        type="button"
        className="cmd-close"
        aria-label="Close"
        onClick={() => onOpenChange?.(false)}
      >
        <X className="h-4 w-4" />
      </button>
      <div className="stage-label">
        <span className="eyebrow">{getStageLabel()}</span>
        {title && isAddSourcesMode && (
          <span style={{ fontSize: "11px", color: "hsl(var(--as-fg-muted))", marginLeft: "8px", fontStyle: "italic" }}>{title}</span>
        )}
      </div>
      <div className="editorial-rule" />

      {isStage0 ? (
        <div className="stage-enter">
          <SearchSeriesStep
            formState={formState}
            setFormState={setFormState}
            setError={setError}
            setIsLoading={setIsLoading}
            setCanProgress={setCanProgress}
            existingSources={existingSources}
          />
        </div>
      ) : (
        <div className="stage-enter">
          <ConfirmSeriesStep
            formState={formState}
            setFormState={setFormState}
            setError={setError}
            setIsLoading={setIsLoading}
            setCanProgress={setCanProgress}
            isAddSourcesMode={isAddSourcesMode}
            existingSources={existingSources}
          />
        </div>
      )}

      {error && (
        <div className="flex items-center gap-2 mb-2 px-3 py-2 rounded text-destructive text-sm" style={{ background: "hsla(0 0% 100% / 0.04)", border: "1px solid hsla(0 84% 60% / 0.25)" }}>
          <AlertTriangle className="h-3.5 w-3.5 flex-shrink-0" />
          <span>{error}</span>
        </div>
      )}

      <div className="cta-row">
        <div className="left-meta font-mono">
          {getLeftMetaCopy()}
        </div>
        <div className="cta-buttons">
          <button
            type="button"
            className="btn-ghost"
            onClick={handlePrev}
            disabled={currentStep === 0}
          >
            Back
          </button>
          <button
            type="button"
            className="btn-primary"
            onClick={isStage0 ? handleNext : handleSubmit}
            disabled={!canProgress || isLoading || isPending}
          >
            {icon}
            {label}
          </button>
        </div>
      </div>
    </div>
  );
}
