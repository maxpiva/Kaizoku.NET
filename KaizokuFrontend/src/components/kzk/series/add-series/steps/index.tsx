"use client";
import { type AddSeriesState } from "@/components/kzk/series/add-series";
import { SearchSeriesStep } from "@/components/kzk/series/add-series/steps/search-series-step";
import { ConfirmSeriesStep } from "@/components/kzk/series/add-series/steps/confirm-series-step";
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
} from "lucide-react";
import React from "react";
import { useAddSeries } from "@/lib/api/hooks/useSeries";
import { useAugmentSeries } from "@/lib/api/hooks/useSearch";
import { useQueryClient } from "@tanstack/react-query";
import { type LinkedSeries, type FullSeries, type ExistingSource, type AugmentedResponse } from "@/lib/api/types";

const steps = {
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

export interface AddSeriesStepsProps {
  onFinish: () => void;
  title?: string;
  existingSources?: ExistingSource[];
  seriesId?: string;
  isAddSourcesMode?: boolean;
}

export function AddSeriesSteps({ 
  onFinish, 
  title, 
  existingSources, 
  seriesId, 
  isAddSourcesMode = false 
}: AddSeriesStepsProps) {  
  // Add debug logging for component lifecycle


  const [formState, setFormState] = React.useState<AddSeriesState>({
    selectedLinkedSeries: [],
    searchKeyword: title || "", // Prefill with title if in Add Sources mode
    allLinkedSeries: [],
    fullSeries: [],
    originalAugmentedResponse: undefined,
    storagePath: undefined,
  });
  const [error, setError] = React.useState<string | null>(null);
  const [isLoading, setIsLoading] = React.useState(false);
  const [canProgress, setCanProgress] = React.useState(false);
  const [pendingNextStep, setPendingNextStep] = React.useState(false);
  // --- Controlled step state ---
  const [currentStep, setCurrentStep] = React.useState(0);
  // Remove useStepper for navigation, but keep for context if needed
  const { isDisabledStep, isError, isLoading: stepperLoading, hasCompletedAllSteps, isLastStep } = useStepper();
  // Log current step index

  const addSeries = useAddSeries();
  const augmentSeries = useAugmentSeries();
  const queryClient = useQueryClient();
  React.useEffect(() => {
    if (pendingNextStep && formState.fullSeries.length > 0) {
      setPendingNextStep(false);
      setCurrentStep((step) => step + 1);
    }
  }, [pendingNextStep, formState.fullSeries]);  const handleNext = async () => {    if (currentStep === 1) { // We're on the confirm step (last step)
      // Final step - add series to library
      try {
        // Filter to only get selected series
        const selectedSeries = formState.fullSeries.filter((series: FullSeries) => series.isSelected);
        
        // Construct the AugmentedResponse with only selected series
        if (!formState.originalAugmentedResponse) {
          throw new Error('Original augmented response not found');
        }
          const finalAugmentedResponse: AugmentedResponse = {
          ...formState.originalAugmentedResponse,
          series: selectedSeries,
          // Use edited storage path if available, otherwise use original
          storageFolderPath: formState.storagePath || formState.originalAugmentedResponse.storageFolderPath,
          // For Add Sources mode, ensure these are set
          existingSeries: isAddSourcesMode || formState.originalAugmentedResponse.existingSeries,
          existingSeriesId: (isAddSourcesMode && seriesId) ? seriesId : formState.originalAugmentedResponse.existingSeriesId
        };
        
        await addSeries.mutateAsync(finalAugmentedResponse);
        
        if (isAddSourcesMode && seriesId) {
          // In Add Sources mode, refresh the specific series data
          await queryClient.invalidateQueries({ queryKey: ['series', 'detail', seriesId] });
        } else {
          // In Add Series mode, refresh the library
          await queryClient.invalidateQueries({ queryKey: ['series', 'library'] });
        }
        
        onFinish();
      } catch (error) {
        console.error('Failed to add series:', error);
      }
    } else if (currentStep === 0) {
      // Moving from search to confirm step - augment selected series
      try {
        // Type-safe filtering
        const allLinkedSeries = formState.allLinkedSeries;
        const selectedLinked: LinkedSeries[] = allLinkedSeries.filter((series: LinkedSeries) => 
          formState.selectedLinkedSeries.includes(series.mihonId ?? series.providerId)
        );        const augmentedResponse = await augmentSeries.mutateAsync(selectedLinked);
        
        // For Add Sources mode, override the existing series properties
        if (isAddSourcesMode && seriesId) {
          augmentedResponse.existingSeries = true;
          augmentedResponse.existingSeriesId = seriesId;
        }
        
        // Helper function to find the preferred series based on language preference
        const findPreferredSeriesIndex = (series: FullSeries[], preferredLanguages: string[] | undefined | null): number => {
          if (!Array.isArray(series) || series.length === 0) return 0;
          if (!Array.isArray(preferredLanguages) || preferredLanguages.length === 0) return 0;
          for (const preferredLang of preferredLanguages) {
            if (typeof preferredLang === 'string') {
              const matchingIndex = series.findIndex(s => 
                typeof s.lang === 'string' && 
                s.lang.toLowerCase() === preferredLang.toLowerCase()
              );
              if (matchingIndex !== -1) {
                return matchingIndex;
              }
            }
          }
          return 0;
        };
        
        const preferredIndex = isAddSourcesMode ? 0 : findPreferredSeriesIndex(augmentedResponse.series, augmentedResponse.preferredLanguages);
        const fullSeriesWithDefaults: FullSeries[] = augmentedResponse.series.map((series: FullSeries, index: number): FullSeries => ({
          ...series,
          // In Add Sources mode, all switches should be off by default
          isStorage: isAddSourcesMode ? false : index === preferredIndex,
          useCover: isAddSourcesMode ? false : index === preferredIndex,
          useTitle: isAddSourcesMode ? false : index === preferredIndex,
        }));        setFormState((prev: AddSeriesState): AddSeriesState => {
          const nextState = { 
            ...prev, 
            fullSeries: fullSeriesWithDefaults,
            originalAugmentedResponse: augmentedResponse, // Store the original augmented response
            storagePath: augmentedResponse.storageFolderPath // Initialize with original storage path
          };
           return nextState;
        });
        setPendingNextStep(true);
       
      } catch (error) {
        console.error('Failed to augment series:', error);
      }
    } else {
      setCurrentStep((step) => step + 1);
    }
  };

  const handlePrev = () => {
    setCurrentStep((step) => Math.max(0, step - 1));
  };

  const Footer = () => (
    <div className="flex w-full justify-end gap-2">
      {currentStep > 0 && (
        <Button
          type="button"
          onClick={handlePrev}
          size="sm"
          className="h-7 gap-1"
          variant="secondary"
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
        className="h-7 gap-1"
        onClick={handleNext}
      >
        {(Boolean(isLoading) || Boolean(augmentSeries.isPending) || Boolean(addSeries.isPending)) ? (
          <LoaderCircle className="h-3.5 w-3.5 animate-spin" />
        ) : currentStep === Object.keys(steps).length - 1 ? (
          <Check className="h-3.5 w-3.5" />
        ) : (
          <ArrowRight className="h-3.5 w-3.5" />
        )}
        <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
          {currentStep === Object.keys(steps).length - 1 ? (isAddSourcesMode ? "Add Sources" : "Add Series") : "Next"}
        </span>
      </Button>
    </div>
  );

  return (
    <div className="flex w-full flex-col gap-4">
      <Stepper
        initialStep={0}
        activeStep={currentStep}
        steps={Object.values(steps)}
        variant="circle"
        orientation="horizontal"
        size="sm"
        responsive={false}
        state={isLoading ? "loading" : error ? "error" : undefined}
      >
        <Step
          label={steps.search.label}
          description={formState.searchKeyword || steps.search.description}
          icon={steps.search.icon}
        >          <SearchSeriesStep
            formState={formState}
            setFormState={setFormState}
            setError={setError}
            setIsLoading={setIsLoading}
            setCanProgress={setCanProgress}
            existingSources={existingSources}
          />
          {error ? (
            <ul className="mb-4 list-disc space-y-1 rounded-lg border bg-destructive/10 p-2 text-[0.8rem] font-medium text-destructive">
              <li className="ml-4">{error}</li>
            </ul>
          ) : null}
        </Step>
        <Step
          label={steps.confirm.label}
          description={formState.fullSeries.length > 0 ? `${formState.fullSeries.length} sources` : steps.confirm.description}
          icon={steps.confirm.icon}
        >          <ConfirmSeriesStep
            formState={formState}
            setFormState={setFormState}
            setError={setError}
            setIsLoading={setIsLoading}
            setCanProgress={setCanProgress}
            isAddSourcesMode={isAddSourcesMode}
            existingSources={existingSources}
          />
          {error ? (
            <ul className="mb-4 list-disc space-y-1 rounded-lg border bg-destructive/10 p-2 text-[0.8rem] font-medium text-destructive">
              <li className="ml-4">{error}</li>
            </ul>
          ) : null}
        </Step>
        <Footer />
      </Stepper>
    </div>
  );
}
