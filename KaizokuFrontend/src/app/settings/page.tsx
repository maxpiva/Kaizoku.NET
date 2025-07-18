"use client";

import React from 'react';
import { SettingsManager } from "@/components/kzk/settings-manager";

export default function SettingsPage() {
  return (
    <SettingsManager
      showHeader={true}
      showSaveButton={true}
      title="Settings"
      description="Configure your Kaizoku.NET application settings"
    />
  );
}
