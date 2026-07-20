"use client";

import { Button, type ButtonProps } from "@/components/ui/button";
import { PlusCircle } from "lucide-react";
import { forwardRef, useState } from "react";
import { type LinkedSeries, type FullSeries, type ExistingSource, type AugmentedResponse } from "@/lib/api/types";

import { AddSeriesSteps } from "@/components/comp/series/add-series/steps";
import {
  Dialog,
  DialogContent,
  DialogTrigger,
} from "@/components/ui/dialog";
import { usePermission } from "@/hooks/use-permission";

export const AddSeriesButton = forwardRef<HTMLButtonElement, ButtonProps>(
  (props, ref) => {
    const canAdd = usePermission('canAddSeries');
    const label = canAdd ? "Add Series" : "Request Series";
    return (
      <Button size="sm" className="h-full gap-1" ref={ref} {...props}>
        <PlusCircle className="h-4 w-4" />
        <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
          {label}
        </span>
      </Button>
    );
  },
);
AddSeriesButton.displayName = "AddSeriesButton";

export interface AddSeriesState {
  selectedLinkedSeries: string[]; // Array of LinkedSeries IDs
  searchKeyword: string;
  allLinkedSeries: LinkedSeries[];
  fullSeries: FullSeries[];
  originalAugmentedResponse?: AugmentedResponse; // Store the original response for final submission
  storagePath?: string; // User-edited storage path
}

export interface AddSeriesProps {
  // Optional props for Add Sources mode
  title?: string;
  existingSources?: ExistingSource[];
  seriesId?: string;
  // Optional props for customizing trigger
  triggerButton?: React.ReactNode;
  open?: boolean;
  onOpenChange?: (open: boolean) => void;
}

export function AddSeries({
  title,
  existingSources,
  seriesId,
  triggerButton,
  open: controlledOpen,
  onOpenChange,
}: AddSeriesProps = {}) {
  const [internalOpen, setInternalOpen] = useState(false);

  // Use controlled or internal state
  const open = controlledOpen !== undefined ? controlledOpen : internalOpen;
  const setOpen = onOpenChange || setInternalOpen;

  // Determine mode booleans
  const isAddSourcesMode = !!(title && existingSources && seriesId);

  const triggerElement = triggerButton || <AddSeriesButton />;

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        {triggerElement}
      </DialogTrigger>
      <DialogContent
        className="bg-transparent border-0 shadow-none p-0 max-h-none overflow-visible w-[calc(100vw-24px)] sm:w-[min(980px,calc(100vw-48px))] max-w-none top-[8dvh] sm:top-[10dvh] translate-y-0 [&>button]:hidden"
        onInteractOutside={(e) => {
          // Allow tap-outside dismiss on mobile; prevent on desktop
          if (!window.matchMedia("(max-width: 640px)").matches) {
            e.preventDefault();
          }
        }}
      >
        <AddSeriesSteps
          onFinish={() => setOpen(false)}
          onOpenChange={setOpen}
          title={title}
          existingSources={existingSources}
          seriesId={seriesId}
          isAddSourcesMode={isAddSourcesMode}
        />
      </DialogContent>
    </Dialog>
  );
}
