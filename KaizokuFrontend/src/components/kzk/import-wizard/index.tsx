"use client";

import React from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Step, Stepper, type StepItem } from "@/components/ui/stepper";
import { ArrowLeft, ArrowRight, Check, LoaderCircle, File, CheckSquare, Flag, Clock } from "lucide-react";
import { useImportWizard } from "@/components/providers/import-wizard-provider";

// Import step components (reusing from setup wizard)
import { ImportLocalStep } from "../setup-wizard/steps/import-local-step";
import { ConfirmImportsStep } from "../setup-wizard/steps/confirm-imports-step";
import { ScheduleUpdatesStep } from "../setup-wizard/steps/schedule-updates-step";
import { FinishStep } from "../setup-wizard/steps/finish-step";

const steps = {
  import: {
    label: "Import Local Files",
    description: "Scan archives",
    icon: File,
  },
  confirm: {
    label: "Confirm Imports",
    description: "Review series",
    icon: CheckSquare,
  },
  schedule: {
    label: "Schedule Updates",
    description: "Configure updates",
    icon: Clock,
  },
  finish: {
    label: "Finish",
    description: "Complete Import",
    icon: Flag,
  },
} satisfies Record<string, StepItem>;

export function ImportWizard() {
  const { isWizardActive, currentStep, totalSteps, nextStep, previousStep, completeWizard } = useImportWizard();
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [canProgress, setCanProgress] = React.useState(false);
  const [disableDownloads, setDisableDownloads] = React.useState(false);

  if (!isWizardActive) {
    return null;
  }

  return (
    <Dialog open={true} onOpenChange={() => { /* Prevent closing */ }} modal>
      <DialogContent
        className="w-[98vw] sm:w-[95vw] md:max-w-[90%] lg:max-w-5xl max-h-[95vh] sm:max-h-[90%] flex flex-col overflow-hidden"
        onInteractOutside={(e) => e.preventDefault()}
        onEscapeKeyDown={(e) => e.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle>Import Wizard</DialogTitle>
          <DialogDescription>
            Import existing manga series from your local files and configure automatic updates.
          </DialogDescription>
        </DialogHeader>

        <div className="flex w-full flex-col gap-4 min-w-0 overflow-hidden">
          <Stepper
            initialStep={0}
            activeStep={currentStep}
            steps={Object.values(steps)}
            variant="circle"
            orientation="horizontal"
            size="md"
            responsive={true}
            state={isLoading ? "loading" : error ? "error" : undefined}
          >
            <Step
              label={steps.import.label}
              description={steps.import.description}
              icon={steps.import.icon}
            >
              <ImportLocalStep
                setError={setError}
                setIsLoading={setIsLoading}
                setCanProgress={setCanProgress}
              />
            </Step>
            {error && currentStep === 0 && (
              <div className="mb-4 list-disc space-y-1 rounded-lg border bg-destructive/10 p-2 text-[0.8rem] font-medium text-destructive">
                {error}
              </div>
            )}

            <Step
              label={steps.confirm.label}
              description={steps.confirm.description}
              icon={steps.confirm.icon}
            >
              <ConfirmImportsStep
                key={`confirm-imports-step-${currentStep}`}
                setError={setError}
                setIsLoading={setIsLoading}
                setCanProgress={setCanProgress}
              />
            </Step>
            {error && currentStep === 1 && (
              <div className="mb-4 list-disc space-y-1 rounded-lg border bg-destructive/10 p-2 text-[0.8rem] font-medium text-destructive">
                {error}
              </div>
            )}

            <Step
              label={steps.schedule.label}
              description={steps.schedule.description}
              icon={steps.schedule.icon}
            >
              <ScheduleUpdatesStep
                setError={setError}
                setIsLoading={setIsLoading}
                setCanProgress={setCanProgress}
                onDownloadOptionChange={setDisableDownloads}
              />
            </Step>
            {error && currentStep === 2 && (
              <div className="mb-4 list-disc space-y-1 rounded-lg border bg-destructive/10 p-2 text-[0.8rem] font-medium text-destructive">
                {error}
              </div>
            )}

            <Step
              label={steps.finish.label}
              description={steps.finish.description}
              icon={steps.finish.icon}
            >
              <FinishStep
                setError={setError}
                setIsLoading={setIsLoading}
                setCanProgress={setCanProgress}
                disableDownloads={disableDownloads}
              />
            </Step>
            {error && currentStep === 3 && (
              <div className="mb-4 list-disc space-y-1 rounded-lg border bg-destructive/10 p-2 text-[0.8rem] font-medium text-destructive">
                {error}
              </div>
            )}
          </Stepper>

          <Footer
            currentStep={currentStep}
            totalSteps={totalSteps}
            canProgress={canProgress}
            isLoading={isLoading}
            onNext={currentStep === totalSteps - 1 ? () => void completeWizard() : () => void nextStep()}
            onPrevious={() => previousStep()}
          />
        </div>
      </DialogContent>
    </Dialog>
  );
}

interface FooterProps {
  currentStep: number;
  totalSteps: number;
  canProgress: boolean;
  isLoading: boolean;
  onNext: () => void;
  onPrevious: () => void;
}

function Footer({ currentStep, totalSteps, canProgress, isLoading, onNext, onPrevious }: FooterProps) {
  const isFirstStep = currentStep === 0;
  const isLastStep = currentStep === totalSteps - 1;

  return (
    <div className="flex flex-col-reverse sm:flex-row sm:justify-between items-center gap-3 sm:gap-2 pt-4 border-t sm:border-t-0">
      <Button
        variant="outline"
        onClick={onPrevious}
        disabled={isFirstStep || isLoading}
        className={`flex items-center gap-2 transition-opacity duration-200 w-full sm:w-auto min-h-[44px] ${isFirstStep ? 'opacity-0 pointer-events-none hidden sm:flex' : ''}`}
        style={isFirstStep ? { visibility: 'hidden' } : {}}
      >
        <ArrowLeft className="h-4 w-4" />
        Previous
      </Button>
      <div className="text-sm text-muted-foreground order-first sm:order-none pb-2 sm:pb-0">
        Step {currentStep + 1} of {totalSteps}
      </div>
      <Button
        onClick={onNext}
        disabled={!canProgress || isLoading}
        className="flex items-center gap-2 w-full sm:w-auto min-h-[44px]"
      >
        {isLoading ? (
          <LoaderCircle className="h-4 w-4 animate-spin" />
        ) : isLastStep ? (
          <Check className="h-4 w-4" />
        ) : (
          <ArrowRight className="h-4 w-4" />
        )}
        {isLastStep ? "Finish" : "Next"}
      </Button>
    </div>
  );
}
