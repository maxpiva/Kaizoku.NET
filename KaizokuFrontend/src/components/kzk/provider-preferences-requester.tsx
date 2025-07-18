"use client";

import React, { useState, useEffect, useCallback } from 'react';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { MultiSelect, type MultiSelectOption } from "@/components/ui/multi-select";
import { Loader2, Settings } from "lucide-react";
import { providerService } from "@/lib/api/services/providerService";
import type { ProviderPreferences, ProviderPreference } from "@/lib/api/types";
import { EntryType } from "@/lib/api/types";

interface ProviderPreferencesRequesterProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  apkName: string;
  providerName?: string;
}

export function ProviderPreferencesRequester({
  open,
  onOpenChange,
  apkName,
  providerName
}: ProviderPreferencesRequesterProps) {  const [preferences, setPreferences] = useState<ProviderPreferences | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [isInitializing, setIsInitializing] = useState(false);
  const [loadedValues, setLoadedValues] = useState<Record<number, unknown>>({});  // Load preferences when dialog opens
  const loadPreferences = useCallback(async () => {
    setLoading(true);
    setIsInitializing(true);
    setError(null);
    
    try {
      const data = await providerService.getProviderPreferences(apkName);
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
  }, [apkName]);  useEffect(() => {
    if (open && apkName) {
      void loadPreferences();
    } else if (!open) {
      // Reset initialization state when dialog closes
      setIsInitializing(false);
      setPreferences(null);
      setError(null);
      setLoadedValues({});
    }
  }, [open, apkName, loadPreferences]);
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
  };  // Update preference value
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
  };  const getCurrentValue = (preference: ProviderPreference): unknown => {
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

    switch (preference.type) {        case EntryType.ComboBox:
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
          <div className="flex items-center space-x-2">
            <Switch
              checked={currentValue as boolean ?? false}
              onCheckedChange={(checked) => updatePreferenceValue(index, checked)}
            />
            <Label className="text-sm">
              {(currentValue as boolean) ? 'Enabled' : 'Disabled'}
            </Label>
          </div>
        );

      default:
        return <div className="text-sm text-muted-foreground">Unknown preference type</div>;
    }
  };
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-7xl max-h-[90vh] overflow-y-auto">        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Settings className="h-5 w-5" />
            {providerName ? `${providerName} Settings` : 'Provider Settings'}
          </DialogTitle>
          <DialogDescription>
            Configure preferences for this provider. Changes will be saved automatically.
          </DialogDescription>
        </DialogHeader>

        {error && (
          <div className="bg-destructive/10 border border-destructive/20 rounded-md p-3">
            <p className="text-sm text-destructive">{error}</p>
          </div>
        )}

        {loading ? (
          <div className="flex items-center justify-center py-8">
            <Loader2 className="h-6 w-6 animate-spin" />
            <span className="ml-2">Loading preferences...</span>
          </div>        ) : preferences ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {preferences.preferences.map((preference, index) => (              <div key={index} className="flex flex-col h-full min-h-[120px]">
                <div className="flex-1 space-y-1 mb-4">
                  <Label className="text-base font-medium">
                    {preference.title}
                  </Label>                  {preference.summary && (
                    <p 
                      className="text-sm text-muted-foreground"
                      dangerouslySetInnerHTML={{ __html: getProcessedSummary(preference) }}
                    />
                  )}</div>
                <div className="mt-auto">
                  {renderPreferenceControl(preference, index)}
                </div>
              </div>
            ))}
          </div>
        ) : null}

        <DialogFooter>
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
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
