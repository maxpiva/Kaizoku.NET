"use client";
import { type AddSeriesState } from "@/components/kzk/series/add-series";
import { SearchSeriesStep } from "@/components/kzk/series/add-series/steps/search-series-step";
import { ConfirmSeriesStep } from "@/components/kzk/series/add-series/steps/confirm-series-step";
import { RequestSeriesStep } from "@/components/kzk/series/add-series/steps/request-series-step";
import { Button } from "@/components/ui/button";
import {
  Step,
  Stepper,
  useStepper,
  type StepItem,
} from "@/components/ui/stepper";
import {
  ArrowLeft,
  ArrowRight,
  Check,
  LoaderCircle,
  Search,
  BookCheck,
  Send,
} from "lucide-react";
import React from "react";
import { useAddSeries } from "@/lib/api/hooks/useSeries";
import { useAugmentSeries } from "@/lib/api/hooks/useSearch";
import { useCreateRequest, useApproveRequest } from "@/lib/api/hooks/useRequests";
import { usePermission } from "@/hooks/use-permission";
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

const addSteps = {
  search: {
    label: "Search",
    description: "Search for series",
    icon: Search,
  },
  confirm: {
    label: "Confirm",
    description: "Selected sources",
    icon: BookCheck,
  },
} satisfies Record<string, StepItem>;

const requestSteps = {
  search: {
    label: "Search",
    description: "Search for series",
    icon: Search,
  },
  request: {
    label: "Request",
    description: "Submit request",
    icon: Send,
  },
} satisfies Record<string, StepItem>;

const approveSteps = {
  confirm: {
    label: "Configure",
    description: "Configure sources",
    icon: BookCheck,
  },
} satisfies Record<string, StepItem>;

export interface AddSeriesStepsProps {
  onFinish: () => void;
  title?: string;
  existingSources?: ExistingSource[];
  seriesId?: string;
  isAddSourcesMode?: boolean;
  approveRequestId?: string;
  preloadedLinkedSeries?: LinkedSeries[];
}

export function AddSeriesSteps({
  onFinish,
  title,
  existingSources,
  seriesId,
  isAddSourcesMode = false,
  approveRequestId,
  preloadedLinkedSeries,
}: AddSeriesStepsProps) {
  const canAddSeries = usePermission('canAddSeries');
  const isApproveMode = !!(approveRequestId && preloadedLinkedSeries);
  const isRequestMode = !canAddSeries && !isAddSourcesMode && !isApproveMode;
  const steps = isApproveMode ? approveSteps : isRequestMode ? requestSteps : addSteps;

  const [formState, setFormState] = React.useState<AddSeriesState>({
    selectedLinkedSeries: [],
    searchKeyword: title || "",
    allLinkedSeries: [],
    fullSeries: [],
    originalAugmentedResponse: undefined,
    storagePath: undefined,
  });
  const [error, setError] = React.useState<string | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [canProgress, setCanProgress] = React.useState(false);
  const [pendingNextStep, setPendingNextStep] = React.useState(false);
  const [requestNote, setRequestNote] = React.useState("");
  const [currentStep, setCurrentStep] = React.useState(0);
  const { isError } = useStepper();

  const addSeries = useAddSeries();
  const augmentSeries = useAugmentSeries();
  const createRequest = useCreateRequest();
  const approveRequest = useApproveRequest();
  const queryClient = useQueryClient();
  const { toast } = useToast();

  React.useEffect(() => {
    if (pendingNextStep && formState.fullSeries.length > 0) {
      setPendingNextStep(false);
      setCurrentStep((step) => step + 1);
    }
  }, [pendingNextStep, formState.fullSeries]);

  // Approve mode: auto-augment preloaded linked series on mount
  const hasAugmentedRef = React.useRef(false);
  React.useEffect(() => {
    if (!isApproveMode || !preloadedLinkedSeries || hasAugmentedRef.current) return;
    hasAugmentedRef.current = true;

    const doAugment = async () => {
      try {
        setIsLoading(true);
        const augmentedResponse = await augmentSeries.mutateAsync(preloadedLinkedSeries);

        const preferredIndex = findPreferredSeriesIndex(augmentedResponse.series, augmentedResponse.preferredLanguages);
        const fullSeriesWithDefaults: FullSeries[] = augmentedResponse.series.map((series: FullSeries, index: number): FullSeries => ({
          ...series,
          isStorage: index === preferredIndex,
          useCover: index === preferredIndex,
          useTitle: index === preferredIndex,
        }));

        setFormState((prev: AddSeriesState): AddSeriesState => ({
          ...prev,
          fullSeries: fullSeriesWithDefaults,
          originalAugmentedResponse: augmentedResponse,
          storagePath: augmentedResponse.storageFolderPath,
        }));
      } catch (err) {
        console.error('Failed to augment series for approval:', err);
        setError('Failed to load series details. Please try again.');
      } finally {
        setIsLoading(false);
      }
    };

    doAugment();
  }, [isApproveMode, preloadedLinkedSeries]);

  const totalSteps = Object.keys(steps).length;

  const handleNext = async () => {
    if (currentStep === totalSteps - 1) {
      // Final step
      if (isRequestMode) {
        // --- Request mode: submit a request ---
        try {
          const selectedLinked = formState.allLinkedSeries.filter((series: LinkedSeries) =>
            formState.selectedLinkedSeries.includes(series.mihonId ?? series.providerId)
          );

          const primary = selectedLinked[0];
          if (selectedLinked.length === 0 || !primary) return;

          // Store the full LinkedSeries array so the admin can augment and add on approve
          await createRequest.mutateAsync({
            title: primary.title,
            description: requestNote || undefined,
            thumbnailUrl: primary.thumbnailUrl ?? undefined,
            providerData: JSON.stringify(selectedLinked),
          });

          toast({
            title: 'Request submitted',
            description: `"${primary.title}" has been requested.`,
            variant: 'success',
          });
          onFinish();
        } catch (err) {
          const msg = err instanceof Error ? err.message : 'Failed to submit request.';
          if (msg.toLowerCase().includes('already') || msg.toLowerCase().includes('exist')) {
            toast({ title: 'Already requested', description: 'This manga has already been requested.', variant: 'destructive' });
          } else {
            toast({ title: 'Failed to submit request', description: msg, variant: 'destructive' });
          }
        }
      } else {
        // --- Add mode (also handles approve mode): add series to library ---
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
            existingSeriesId: (isAddSourcesMode && seriesId) ? seriesId : formState.originalAugmentedResponse.existingSeriesId
          };

          await addSeries.mutateAsync(finalAugmentedResponse);

          // If approving a request, mark it as approved after successful add
          if (isApproveMode && approveRequestId) {
            try {
              await approveRequest.mutateAsync({ id: approveRequestId });
            } catch (err) {
              console.error('Series added but failed to update request status:', err);
            }
          }

          if (isAddSourcesMode && seriesId) {
            await queryClient.invalidateQueries({ queryKey: ['series', 'detail', seriesId] });
          } else {
            await queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
          }

          onFinish();
        } catch (error) {
          console.error('Failed to add series:', error);
          const msg = error instanceof Error ? error.message : 'Failed to add series.';
          toast({ title: 'Failed to add series', description: msg, variant: 'destructive' });
        }
      }
    } else if (currentStep === 0 && !isApproveMode) {
      if (isRequestMode) {
        // Request mode: skip augmentation, go straight to request step
        setCurrentStep(1);
      } else {
        // Add mode: augment selected series
        try {
          const allLinkedSeries = formState.allLinkedSeries;
          const selectedLinked: LinkedSeries[] = allLinkedSeries.filter((series: LinkedSeries) =>
            formState.selectedLinkedSeries.includes(series.mihonId ?? series.providerId)
          );
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
            storagePath: augmentedResponse.storageFolderPath
          }));
          setPendingNextStep(true);

        } catch (error) {
          console.error('Failed to augment series:', error);
        }
      }
    } else {
      setCurrentStep((step) => step + 1);
    }
  };

  const handlePrev = () => {
    setCurrentStep((step) => Math.max(0, step - 1));
  };

  const isPending = Boolean(augmentSeries.isPending) || Boolean(addSeries.isPending) || Boolean(createRequest.isPending) || Boolean(approveRequest.isPending);
  const isLastStep = currentStep === totalSteps - 1;

  const getButtonLabel = () => {
    if (isLastStep) {
      if (isRequestMode) return "Submit Request";
      if (isApproveMode) return "Approve & Add";
      if (isAddSourcesMode) return "Add Sources";
      return "Add Series";
    }
    return "Next";
  };

  const Footer = () => (
    <div className="flex w-full justify-end gap-2 pt-2 border-t border-border mt-2">
      {currentStep > 0 && (
        <Button
          type="button"
          onClick={handlePrev}
          size="sm"
          className="gap-1"
          variant="outline"
        >
          <ArrowLeft className="h-3.5 w-3.5" />
          <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
            Back
          </span>
        </Button>
      )}
      <Button
        type="button"
        disabled={
          !canProgress || Boolean(isLoading) || Boolean(isError)
        }
        size="sm"
        className="gap-1"
        onClick={handleNext}
      >
        {isPending ? (
          <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
        ) : isLastStep ? (
          isRequestMode ? <Send className="h-3.5 w-3.5" /> : <Check className="h-3.5 w-3.5" />
        ) : (
          <ArrowRight className="h-3.5 w-3.5" />
        )}
        <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
          {getButtonLabel()}
        </span>
      </Button>
    </div>
  );

  const stepValues = Object.values(steps);

  const errorBlock = error ? (
    <ul className="mb-4 list-disc space-y-1 rounded-lg border bg-destructive/10 p-2 text-[0.8rem] font-medium text-destructive">
      <li className="ml-4">{error}</li>
    </ul>
  ) : null;

  // Approve mode: single-step confirm (no search)
  if (isApproveMode) {
    const confirmStep = stepValues[0]!;
    return (
      <div className="flex w-full flex-col gap-4">
        <Stepper
          initialStep={0}
          activeStep={currentStep}
          steps={stepValues}
          variant="circle"
          orientation="horizontal"
          size="sm"
          responsive={false}
          state={isLoading ? "loading" : error ? "error" : undefined}
        >
          <Step
            label={confirmStep.label}
            description={formState.fullSeries.length > 0 ? `${formState.fullSeries.length} sources` : confirmStep.description}
            icon={confirmStep.icon}
          >
            <ConfirmSeriesStep
              formState={formState}
              setFormState={setFormState}
              setError={setError}
              setIsLoading={setIsLoading}
              setCanProgress={setCanProgress}
              isAddSourcesMode={false}
              existingSources={existingSources}
            />
            {errorBlock}
          </Step>
          <Footer />
        </Stepper>
      </div>
    );
  }

  // Normal two-step flow: Search → Confirm/Request
  const searchStep = stepValues[0]!;
  const secondStep = stepValues[1]!;

  return (
    <div className="flex w-full flex-col gap-4">
      <Stepper
        initialStep={0}
        activeStep={currentStep}
        steps={stepValues}
        variant="circle"
        orientation="horizontal"
        size="sm"
        responsive={false}
        state={isLoading ? "loading" : error ? "error" : undefined}
      >
        <Step
          label={searchStep.label}
          description={formState.searchKeyword || searchStep.description}
          icon={searchStep.icon}
        >
          <SearchSeriesStep
            formState={formState}
            setFormState={setFormState}
            setError={setError}
            setIsLoading={setIsLoading}
            setCanProgress={setCanProgress}
            existingSources={existingSources}
          />
          {errorBlock}
        </Step>
        <Step
          label={secondStep.label}
          description={
            isRequestMode
              ? (formState.selectedLinkedSeries.length > 0 ? `${formState.selectedLinkedSeries.length} selected` : secondStep.description)
              : (formState.fullSeries.length > 0 ? `${formState.fullSeries.length} sources` : secondStep.description)
          }
          icon={secondStep.icon}
        >
          {isRequestMode ? (
            <RequestSeriesStep
              formState={formState}
              setCanProgress={setCanProgress}
              requestNote={requestNote}
              onRequestNoteChange={setRequestNote}
            />
          ) : (
            <ConfirmSeriesStep
              formState={formState}
              setFormState={setFormState}
              setError={setError}
              setIsLoading={setIsLoading}
              setCanProgress={setCanProgress}
              isAddSourcesMode={isAddSourcesMode}
              existingSources={existingSources}
            />
          )}
          {errorBlock}
        </Step>
        <Footer />
      </Stepper>
    </div>
  );
}
