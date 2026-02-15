"use client";

import React, { useState } from 'react';
import { Button } from "@/components/ui/button";
import { Settings } from "lucide-react";
import { ProviderPreferencesRequester } from "./provider-preferences-requester";

interface ProviderSettingsButtonProps {
  pkgName: string;
  providerName?: string;
  variant?: "default" | "outline" | "secondary" | "ghost" | "link" | "destructive";
  size?: "default" | "sm" | "lg" | "icon";
  className?: string;
}

export function ProviderSettingsButton({
  pkgName,
  providerName,
  variant = "outline",
  size = "sm",
  className = ""
}: ProviderSettingsButtonProps) {
  const [preferencesOpen, setPreferencesOpen] = useState(false);

  return (
    <>
      <Button
        variant={variant}
        size={size}
        className={className}
        onClick={() => setPreferencesOpen(true)}
      >
        <Settings className="h-4 w-4" />
        {size !== "icon" && <span className="ml-1">Settings</span>}
      </Button>

      <ProviderPreferencesRequester
        open={preferencesOpen}
        onOpenChange={setPreferencesOpen}
        pkgName={pkgName}
        providerName={providerName}
      />
    </>
  );
}
