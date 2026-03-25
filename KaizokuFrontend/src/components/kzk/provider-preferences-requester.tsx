"use client";

import React, { useState, useEffect, useCallback } from 'react';
import { Dialog, DialogContent, DialogTitle, DialogDescription } from "@/components/ui/dialog";
import { Drawer, DrawerContent, DrawerHeader, DrawerTitle, DrawerDescription, DrawerFooter } from "@/components/ui/drawer";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { MultiSelect, type MultiSelectOption } from "@/components/ui/multi-select";
import { Loader2, Settings } from "lucide-react";
import { useMediaQuery } from "@/hooks/use-media-query";
import { providerService } from "@/lib/api/services/providerService";
import type { ProviderPreferences, ProviderPreference } from "@/lib/api/types";
import { EntryType } from "@/lib/api/types";

interface ProviderPreferencesRequesterProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  pkgName: string;
  providerName?: string;
}

export function ProviderPreferencesRequester({
  open,
  onOpenChange,
  pkgName,
  providerName
}: ProviderPreferencesRequesterProps) {
  const [preferences, setPreferences] = useState<ProviderPreferences | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isInitializing, setIsInitializing] = useState(false);
  const [loadedValues, setLoadedValues] = useState<Record<number, unknown>>({});

  const isDesktop = useMediaQuery("(min-width: 768px)");

  // Load preferences when dialog opens
  const loadPreferences = useCallback(async () => {
    setLoading(true);
    setIsInitializing(true);
    setError(null);

    try {
      const data = await providerService.getProviderPreferences(pkgName);
      setPreferences(data);

      // Store the loaded values to prevent overwrites
      const initialValues: Record<number, unknown> = {};
      data.preferences.forEach((pref, index) => {
        initialValues[index] = pref.currentValue ?? pref.defaultValue;
      });
      setLoadedValues(initialValues);

      // Allow a brief delay to ensure React has processed the state update
      setTimeout(() => setIsInitializing(false), 100);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load preferences');
      console.error('Failed to load preferences:', err);
      setIsInitializing(false);
    } finally {
      setLoading(false);
    }
  }, [pkgName]);

  useEffect(() => {
    if (open && pkgName) {
      void loadPreferences();
    } else if (!open) {
      // Reset initialization state when dialog closes
      setIsInitializing(false);
      setPreferences(null);
      setError(null);
      setLoadedValues({});
    }
  }, [open, pkgName, loadPreferences]);

  // Save preferences
  const handleSave = async () => {
    if (!preferences) return;

    setSaving(true);
    setError(null);

    try {
      await providerService.setProviderPreferences(preferences);
      onOpenChange(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save preferences');
      console.error('Failed to save preferences:', err);
    } finally {
      setSaving(false);
    }
  };

  // Update preference value
  const updatePreferenceValue = (index: number, newValue: unknown) => {
    if (!preferences) return;

    // Prevent updates during initialization to avoid overwriting loaded values
    if (isInitializing) {
      return;
    }

    const currentPref = preferences.preferences[index];
    if (!currentPref) return;

    const currentVal = getCurrentValue(currentPref);
    const loadedVal = loadedValues[index];

    // If we're trying to set the value to what was originally loaded, ignore it
    if (loadedVal !== undefined && newValue === loadedVal && currentVal === loadedVal) {
      return;
    }

    // Only update if the value actually changed to prevent unnecessary overwrites
    if (currentVal === newValue) return;

    const updatedPreferences = {
      ...preferences,
      preferences: preferences.preferences.map((pref, i) =>
        i === index ? { ...pref, currentValue: newValue } : pref
      )
    };

    setPreferences(updatedPreferences);
  };

  const getCurrentValue = (preference: ProviderPreference): unknown => {
    return preference.currentValue ?? preference.defaultValue;
  };

  // Get the correct value for ComboBox, ensuring it matches entryValues
  const getCurrentComboBoxValue = (preference: ProviderPreference): string => {
    let currentVal = preference.currentValue;

    // If currentValue is null, undefined, or empty, use defaultValue
    if (currentVal === null || currentVal === undefined || currentVal === '') {
      currentVal = preference.defaultValue;
    }

    // Ensure the value exists in entryValues, otherwise use the first one
    if (preference.entryValues && preference.entryValues.length > 0) {
      if (preference.entryValues.includes(currentVal as string)) {
        return currentVal as string;
      }
      // If currentValue is not in entryValues, return the first valid value
      return preference.entryValues[0] ?? '';
    }

    return currentVal as string || '';
  };

  // Process summary text for ComboBox preferences, replacing %s with selected display value
  const getProcessedSummary = (preference: ProviderPreference): string => {
    if (!preference.summary) return '';

    if (preference.type === EntryType.ComboBox && preference.summary.includes('%s')) {
      const currentValue = getCurrentValue(preference) as string;
      if (currentValue && preference.entries && preference.entryValues) {
        // Find the index of the current value in entryValues
        const valueIndex = preference.entryValues.indexOf(currentValue);
        if (valueIndex !== -1 && valueIndex < preference.entries.length) {
          // Replace %s with the corresponding display entry
          const displayValue = preference.entries[valueIndex];
          return preference.summary.replace(/%s/g, displayValue ?? '').replace(/\n/g, '<br/>');
        }
      }
      // If no match found, replace %s with current value or empty string
      return preference.summary.replace(/%s/g, String(currentValue ?? '')).replace(/\n/g, '<br/>');
    }

    return preference.summary.replace(/\n/g, '<br/>');
  };

  const renderPreferenceControl = (preference: ProviderPreference, index: number) => {
    const currentValue = getCurrentValue(preference);

    switch (preference.type) {
      case EntryType.ComboBox:
        const comboBoxValue = getCurrentComboBoxValue(preference);

        return (
          <Select
            value={comboBoxValue}
            onValueChange={(value) => {
              updatePreferenceValue(index, value);
            }}
          >
            <SelectTrigger>
              <SelectValue placeholder="Select an option" />
            </SelectTrigger>
            <SelectContent>
              {preference.entries?.map((entry, entryIndex) => (
                <SelectItem
                  key={entryIndex}
                  value={preference.entryValues?.[entryIndex] ?? entry}
                >
                  {entry}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        );
      case EntryType.ComboCheckBox:
        const selectedValues = (currentValue as string[]) ?? [];
        const options: MultiSelectOption[] = preference.entries?.map((entry, index) => ({
          value: preference.entryValues?.[index] ?? entry,
          label: entry
        })) ?? [];

        return (
          <MultiSelect
            options={options}
            selectedValues={selectedValues}
            onSelectionChange={(values) => updatePreferenceValue(index, values)}
            placeholder="Select options..."
          />
        );

      case EntryType.TextBox:
        return (
          <Input
            value={currentValue as string ?? ''}
            onChange={(e) => updatePreferenceValue(index, e.target.value)}
            placeholder="Enter value"
          />
        );

      case EntryType.Switch:
        return (
          <Switch
            checked={currentValue as boolean ?? false}
            onCheckedChange={(checked) => updatePreferenceValue(index, checked)}
          />
        );

      default:
        return <div className="text-sm text-muted-foreground">Unknown preference type</div>;
    }
  };

  const titleText = providerName ? `${providerName} Settings` : 'Provider Settings';
  const descriptionText = "Configure preferences for this provider. Changes will be saved automatically.";

  const errorContent = error ? (
    <div className="bg-destructive/10 border border-destructive/20 rounded-md p-3">
      <p className="text-sm text-destructive">{error}</p>
    </div>
  ) : null;

  const loadingContent = (
    <div className="flex items-center justify-center py-8">
      <Loader2 className="h-6 w-6 animate-spin" />
      <span className="ml-2">Loading preferences...</span>
    </div>
  );

  if (isDesktop) {
    return (
      <Dialog open={open} onOpenChange={onOpenChange}>
        <DialogContent className="w-[95vw] max-w-3xl max-h-[85vh] flex flex-col overflow-hidden p-0">
          <div className="px-5 py-3.5 border-b border-border shrink-0">
            <DialogTitle className="flex items-center gap-2 text-[15px]">
              <Settings className="h-5 w-5" />
              {titleText}
            </DialogTitle>
            <DialogDescription className="text-xs text-muted-foreground mt-1">
              {descriptionText}
            </DialogDescription>
          </div>

          <div className="flex-1 overflow-y-auto">
            {errorContent}

            {loading ? loadingContent : preferences ? (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-px bg-border">
                {preferences.preferences.map((preference, index) => (
                  preference.type === EntryType.Switch ? (
                    <div key={index} className="bg-background p-4 flex flex-col gap-1.5">
                      <div className="flex items-center justify-between gap-3">
                        <div>
                          <Label className="text-[13px] font-semibold">{preference.title}</Label>
                          {preference.summary && (
                            <p
                              className="text-[11.5px] text-muted-foreground"
                              dangerouslySetInnerHTML={{ __html: getProcessedSummary(preference) }}
                            />
                          )}
                        </div>
                        {renderPreferenceControl(preference, index)}
                      </div>
                    </div>
                  ) : (
                    <div key={index} className="bg-background p-4 flex flex-col gap-1.5">
                      <Label className="text-[13px] font-semibold">{preference.title}</Label>
                      {preference.summary && (
                        <p
                          className="text-[11.5px] text-muted-foreground"
                          dangerouslySetInnerHTML={{ __html: getProcessedSummary(preference) }}
                        />
                      )}
                      <div>{renderPreferenceControl(preference, index)}</div>
                    </div>
                  )
                ))}
              </div>
            ) : null}
          </div>

          <div className="px-5 py-3 border-t border-border flex items-center justify-end gap-2 bg-card/50 shrink-0">
            <Button variant="outline" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button
              onClick={handleSave}
              disabled={loading || saving || !preferences}
            >
              {saving && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
              Save Preferences
            </Button>
          </div>
        </DialogContent>
      </Dialog>
    );
  }

  return (
    <Drawer open={open} onOpenChange={onOpenChange}>
      <DrawerContent className="max-h-[92dvh] flex flex-col">
        <DrawerHeader className="text-left pb-1">
          <DrawerTitle className="flex items-center gap-2">
            <Settings className="h-5 w-5" />
            {titleText}
          </DrawerTitle>
          <DrawerDescription>
            {descriptionText}
          </DrawerDescription>
        </DrawerHeader>

        <div className="flex-1 overflow-y-auto" data-vaul-no-drag>
          {errorContent}

          {loading ? loadingContent : preferences ? (
            <div>
              {preferences.preferences.map((preference, index) => (
                preference.type === EntryType.Switch ? (
                  <div key={index} className="px-4 py-3 border-b border-border/50">
                    <div className="flex items-center justify-between gap-3 mb-0.5">
                      <Label className="text-[13px] font-semibold">{preference.title}</Label>
                      {renderPreferenceControl(preference, index)}
                    </div>
                    {preference.summary && (
                      <p
                        className="text-[11.5px] text-muted-foreground"
                        dangerouslySetInnerHTML={{ __html: getProcessedSummary(preference) }}
                      />
                    )}
                  </div>
                ) : (
                  <div key={index} className="px-4 py-3 border-b border-border/50">
                    <Label className="text-[13px] font-semibold">{preference.title}</Label>
                    {preference.summary && (
                      <p
                        className="text-[11.5px] text-muted-foreground"
                        dangerouslySetInnerHTML={{ __html: getProcessedSummary(preference) }}
                      />
                    )}
                    <div className="mt-1.5">{renderPreferenceControl(preference, index)}</div>
                  </div>
                )
              ))}
            </div>
          ) : null}
        </div>

        <DrawerFooter className="pb-[max(1rem,env(safe-area-inset-bottom))]">
          <Button
            onClick={handleSave}
            disabled={loading || saving || !preferences}
            className="w-full"
          >
            {saving && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
            Save Preferences
          </Button>
          <Button variant="outline" onClick={() => onOpenChange(false)} className="w-full">
            Cancel
          </Button>
        </DrawerFooter>
      </DrawerContent>
    </Drawer>
  );
}
