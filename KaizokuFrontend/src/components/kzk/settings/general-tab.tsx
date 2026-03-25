"use client";

import React from 'react';
import { SettingsManager } from '@/components/kzk/settings-manager';

export function GeneralTab() {
  return (
    <div className="space-y-1">
      <div className="mb-6">
        <h2 className="text-lg font-semibold text-foreground">General Settings</h2>
        <p className="text-sm text-muted-foreground">
          Configure storage, downloads, schedules, proxy, and content settings.
        </p>
      </div>
      <SettingsManager
        showHeader={false}
        showSaveButton={true}
        className="space-y-4"
      />
    </div>
  );
}
