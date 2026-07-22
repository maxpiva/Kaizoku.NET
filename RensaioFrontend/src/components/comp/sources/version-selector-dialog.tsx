"use client";

import React, { useState } from "react";
import { Wrench } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
  DialogClose,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { type Provider, type ExtensionEntry } from "@/lib/api/types";
import { getInstalledRepoEntries } from "./lib";

interface VersionSelectorDialogProps {
  extension: Provider;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onConfirm: (version: string, autoUpdate: boolean) => void;
  isSubmitting: boolean;
}

export function VersionSelectorDialog({
  extension,
  open,
  onOpenChange,
  onConfirm,
  isSubmitting,
}: VersionSelectorDialogProps) {
  const entries = getInstalledRepoEntries(extension) ?? [];
  const currentEntry = entries[extension.activeEntry] ?? entries[0];

  const [selectedVersion, setSelectedVersion] = useState(currentEntry?.version ?? "");
  const [autoUpdate, setAutoUpdate] = useState(extension.autoUpdate);

  // Reset state when dialog opens
  React.useEffect(() => {
    if (open && currentEntry) {
      setSelectedVersion(currentEntry.version);
      setAutoUpdate(extension.autoUpdate);
    }
  }, [open, currentEntry?.version, extension.autoUpdate]);

  const handleConfirm = () => {
    onConfirm(selectedVersion, autoUpdate);
  };

  const formatOptionLabel = (entry: ExtensionEntry): string => {
    const label = `v${entry.version}`;
    return entry.isLocal ? `${label} Local` : label;
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-[400px]">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Wrench className="h-4 w-4" />
            Version — {extension.name}
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-5 py-3">
          {/* Version selector */}
          <div className="space-y-2">
            <Label htmlFor="version-select">Version</Label>
            <Select
              value={selectedVersion}
              onValueChange={setSelectedVersion}
            >
              <SelectTrigger id="version-select" className="w-full">
                <SelectValue placeholder="Select version" />
              </SelectTrigger>
              <SelectContent>
                {entries.map((entry) => (
                  <SelectItem key={entry.id} value={entry.version}>
                    {formatOptionLabel(entry)}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {/* Auto-update toggle */}
          <div className="flex items-center justify-between">
            <Label htmlFor="auto-update-switch" className="cursor-pointer">
              Auto-update
            </Label>
            <Switch
              id="auto-update-switch"
              checked={autoUpdate}
              onCheckedChange={setAutoUpdate}
            />
          </div>
        </div>

        <DialogFooter>
          <DialogClose asChild>
            <Button variant="outline" disabled={isSubmitting}>
              Cancel
            </Button>
          </DialogClose>
          <Button onClick={handleConfirm} disabled={isSubmitting}>
            {isSubmitting ? "Saving…" : "Confirm"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
