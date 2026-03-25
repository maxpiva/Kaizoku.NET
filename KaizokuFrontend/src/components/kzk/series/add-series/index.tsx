"use client";

import { Button, type ButtonProps } from "@/components/ui/button";
import { PlusCircle } from "lucide-react";
import { forwardRef, useState } from "react";
import { type LinkedSeries, type FullSeries, type ExistingSource, type AugmentedResponse } from "@/lib/api/types";

import { AddSeriesSteps } from "@/components/kzk/series/add-series/steps";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  Drawer,
  DrawerContent,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from "@/components/ui/drawer";
import { useMediaQuery } from "@/hooks/use-media-query";

export const AddSeriesButton = forwardRef<HTMLButtonElement, ButtonProps>(
  (props, ref) => {
    return (
      <Button size="sm" className="h-full gap-1" ref={ref} {...props}>
        <PlusCircle className="h-4 w-4" />
        <span className="sr-only sm:not-sr-only sm:whitespace-nowrap">
          Add Series
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
  const isDesktop = useMediaQuery("(min-width: 768px)");

  // Use controlled or internal state
  const open = controlledOpen !== undefined ? controlledOpen : internalOpen;
  const setOpen = onOpenChange || setInternalOpen;
    // Determine if this is Add Sources mode
  const isAddSourcesMode = !!(title && existingSources && seriesId);

  // Dialog/Drawer content
  const dialogTitle = isAddSourcesMode
    ? `Add New Sources to '${title}'`
    : "Add new series";
  const dialogDescription = isAddSourcesMode
    ? "Search and add new sources to your Series."
    : "Search for and add new series to your library.";

  const triggerElement = triggerButton || <AddSeriesButton />;
  if (isDesktop) {
    return (
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogTrigger asChild>
          {triggerElement}
        </DialogTrigger>
        <DialogContent
          className="w-[95vw] max-w-4xl max-h-[85vh] overflow-y-auto p-0"
          onInteractOutside={(e) => {
            e.preventDefault();
          }}
        >
          <DialogHeader className="px-5 pt-5 pb-0">
            <DialogTitle>{dialogTitle}</DialogTitle>
            <DialogDescription>
              {dialogDescription}
            </DialogDescription>
          </DialogHeader>
          <div className="px-5 pb-5">
          <AddSeriesSteps
            onFinish={() => setOpen(false)}
            title={title}
            existingSources={existingSources}
            seriesId={seriesId}
            isAddSourcesMode={isAddSourcesMode}
          />
          </div>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Drawer open={open} onOpenChange={setOpen}>
      <DrawerTrigger asChild>
        {triggerElement}
      </DrawerTrigger>
      <DrawerContent className="max-h-[92dvh] flex flex-col">
        <DrawerHeader className="text-left pb-2">
          <DrawerTitle>{dialogTitle}</DrawerTitle>
        </DrawerHeader>
        <div className="flex-1 overflow-y-auto overscroll-contain px-4 pb-[max(1rem,env(safe-area-inset-bottom))]" data-vaul-no-drag>
          <AddSeriesSteps
            onFinish={() => setOpen(false)}
            title={title}
            existingSources={existingSources}
            seriesId={seriesId}
            isAddSourcesMode={isAddSourcesMode}
          />
        </div>
      </DrawerContent>
    </Drawer>
  );
}
