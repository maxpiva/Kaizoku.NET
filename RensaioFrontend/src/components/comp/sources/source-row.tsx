"use client";

import React, { useState, useCallback } from 'react';
import ReactCountryFlag from "react-country-flag";
import { Wrench } from "lucide-react";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";
import { type Provider } from "@/lib/api/types";
import {
  getPrimaryLanguage,
  getExtensionLanguages,
  getExtensionVersion,
  isExtensionNsfw,
  isActiveEntryLocal,
  getEntryCount,
} from "./lib";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { SourceThumb } from "./source-thumb";
import { RowActionsInstalled } from "./row-actions-installed";
import { RowActionsAvailable } from "./row-actions-available";
import { VersionSelectorDialog } from "./version-selector-dialog";
import { providerService } from "@/lib/api/services/providerService";
import { useToast } from "@/hooks/use-toast";

interface SourceRowProps {
  extension: Provider;
  mode: 'installed' | 'available';
  onInstall?: (pkgName: string) => void;
  onUninstall?: (pkgName: string) => void;
  isLoading?: boolean;
  showNsfwIndicator?: boolean;
  /** Called after a version switch completes so the parent can refresh its list */
  onVersionChanged?: () => void;
}

function formatLanguageMeta(extension: Provider): string {
  const langs = getExtensionLanguages(extension);
  const version = getExtensionVersion(extension);
  const versionStr = version ? `v${version}` : '';

  let langStr = '';
  if (langs.length === 0) {
    langStr = '';
  } else if (langs.length === 1) {
    langStr = langs[0] ?? '';
  } else if (langs.length <= 3) {
    langStr = langs.join(', ');
  } else {
    langStr = `Multi (${langs.length})`;
  }

  if (versionStr && langStr) return `${versionStr} · ${langStr}`;
  if (versionStr) return versionStr;
  return langStr;
}

export function SourceRow({
  extension,
  mode,
  onInstall,
  onUninstall,
  isLoading = false,
  showNsfwIndicator = true,
  onVersionChanged,
}: SourceRowProps) {
  const { toast } = useToast();
  const primaryLanguage = getPrimaryLanguage(extension);
  const countryCode = getCountryCodeForLanguage(primaryLanguage);
  const isNsfw = showNsfwIndicator && isExtensionNsfw(extension);
  const meta = formatLanguageMeta(extension);
  const isFailing =
    mode === 'installed' &&
    (extension.isBroken || extension.isDead);

  // -- Version selector state --
  const [versionDialogOpen, setVersionDialogOpen] = useState(false);
  const [versionSubmitting, setVersionSubmitting] = useState(false);

  const isLocalEntry = mode === 'installed' && isActiveEntryLocal(extension);
  const entryCount = mode === 'installed' ? getEntryCount(extension) : 0;
  const showWrench = mode === 'installed' && entryCount > 1;

  const handleVersionConfirm = useCallback(async (version: string, autoUpdate: boolean) => {
    setVersionSubmitting(true);
    try {
      await providerService.setProviderVersion(extension.package, version, autoUpdate);
      toast({ title: `Version set to v${version}`, variant: 'success' });
      setVersionDialogOpen(false);
      onVersionChanged?.();
    } catch (err) {
      console.error('Failed to set version:', err);
      toast({ title: 'Failed to set version', variant: 'destructive' });
    } finally {
      setVersionSubmitting(false);
    }
  }, [extension.package, onVersionChanged, toast]);

  const rowContent = (
    <div
      className={`src-row${isFailing ? ' is-failing' : ''}`}
    >
      {/* Thumbnail — responsive size via CSS classes on wrapper */}
      <div className="md:hidden">
        <SourceThumb extension={extension} size="sm" />
      </div>
      <div className="hidden md:block">
        <SourceThumb extension={extension} size="md" />
      </div>

      {/* Middle: name + meta */}
      <div className="flex-1 min-w-0">
        {/* Line 1: name + flag + badges */}
        <div className="flex items-center gap-1.5 md:gap-2">
          {isFailing && <span className="dot-fail" aria-hidden="true" />}
          <span className="font-semibold text-[14px] md:text-[15px] truncate text-foreground">
            {extension.name}
          </span>
          <ReactCountryFlag
            countryCode={countryCode}
            svg
            style={{ width: '16px', height: '12px', flexShrink: 0 }}
            title={`${primaryLanguage.toUpperCase()} (${countryCode})`}
          />
          {isNsfw && <span className="nsfw-pill">18+</span>}
        </div>

        {/* Line 2: version · languages (muted) + Local badge + wrench */}
        <div className="flex items-center gap-1.5 mt-0.5 min-w-0">
          {meta ? (
            <span className="text-[12px] md:text-[13px] text-muted-foreground truncate">
              {meta}
            </span>
          ) : null}
          {isLocalEntry && (
            <span className="text-[11px] px-1.5 py-0.5 rounded bg-yellow-500/20 text-yellow-600 dark:text-yellow-400 font-medium whitespace-nowrap leading-none">
              Local
            </span>
          )}
          {showWrench && (
            <button
              className="inline-flex items-center justify-center w-5 h-5 rounded text-muted-foreground hover:text-foreground hover:bg-white/10 transition-colors flex-shrink-0"
              onClick={() => setVersionDialogOpen(true)}
              aria-label={`Select version for ${extension.name}`}
            >
              <Wrench className="h-3.5 w-3.5" />
            </button>
          )}
        </div>
      </div>

      {/* Right: action area */}
      <div className="flex items-center gap-1.5 shrink-0">
        {mode === 'installed' && onUninstall ? (
          <RowActionsInstalled
            extension={extension}
            onUninstall={onUninstall}
            isLoading={isLoading}
          />
        ) : mode === 'available' && onInstall ? (
          <RowActionsAvailable
            extension={extension}
            onInstall={onInstall}
            isLoading={isLoading}
          />
        ) : null}
      </div>
    </div>
  );

  return (
    <>
      {/* Wrap failing installed rows in a Tooltip to expose the error message */}
      {isFailing ? (
        <Tooltip>
          <TooltipTrigger asChild>
            {rowContent}
          </TooltipTrigger>
          <TooltipContent side="top" className="max-w-xs text-xs">
            Source is broken or unreachable
          </TooltipContent>
        </Tooltip>
      ) : (
        rowContent
      )}

      {/* Version selector dialog */}
      {mode === 'installed' && (
        <VersionSelectorDialog
          extension={extension}
          open={versionDialogOpen}
          onOpenChange={setVersionDialogOpen}
          onConfirm={handleVersionConfirm}
          isSubmitting={versionSubmitting}
        />
      )}
    </>
  );
}
