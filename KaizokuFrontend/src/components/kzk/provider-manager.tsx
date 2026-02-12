"use client";

import React, { useState, useEffect, useMemo, useRef } from "react";
import {
  Card,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Separator } from "@/components/ui/separator";
import { Download, Trash2, Search, Upload } from "lucide-react";
import ReactCountryFlag from "react-country-flag";
import { providerService } from "@/lib/api/services/providerService";
import { type Provider } from "@/lib/api/types";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { LazyImage } from "@/components/ui/lazy-image";
import { ProviderSettingsButton } from "@/components/kzk/provider-settings-button";
import { ProviderPreferencesRequester } from "@/components/kzk/provider-preferences-requester";
import { Checkbox } from "../ui/checkbox";
import { Label } from "@/components/ui/label";
import { MultiSelectLanguages } from "@/components/ui/multi-select-languages";
import { useSettings } from "@/lib/api/hooks/useSettings";

interface ProviderCardProps {
  extension: Provider;
  onInstall?: (pkgName: string) => void;
  onUninstall?: (pkgName: string) => void;
  isLoading?: boolean;
  isCompact?: boolean;
  showNsfwIndicator?: boolean;
}

function ProviderCard({
  extension,
  onInstall,
  onUninstall,
  isLoading = false,
  isCompact = false,
  showNsfwIndicator = true,
}: ProviderCardProps) {
  const handleAction = () => {
    if (extension.installed && onUninstall) {
      onUninstall(extension.pkgName);
    } else if (!extension.installed && onInstall) {
      onInstall(extension.pkgName);
    }
  };

  const countryCode = getCountryCodeForLanguage(extension.lang);

  return (
    <Card className="w-full">
      <CardHeader className={isCompact ? "p-3" : "p-2 pr-4"}>
        <div className="flex items-center justify-between">
          <div className="flex min-w-0 flex-1 items-center gap-3">
            <div className="relative flex-shrink-0">
              <LazyImage
                src={extension.iconUrl}
                alt={`${extension.name} icon`}
                className={`${isCompact ? "h-12 w-12" : "h-20 w-20"} rounded-lg object-cover`}
                fallbackSrc="/kaizoku.net.png"
                loading="lazy"
              />
              {showNsfwIndicator && extension.isNsfw && (
                <div className="absolute -top-1 -right-1">
                  {isCompact ? (
                    <div className="rounded-full bg-red-500 px-1 text-[9px] text-white">
                      18+
                    </div>
                  ) : (
                    <svg
                      className="h-5 w-5 text-red-500"
                      xmlns="http://www.w3.org/2000/svg"
                      data-name="Layer 2"
                      viewBox="0 0 1850 1850"
                    >
                      <g fill="currentColor" data-name="Layer 1">
                        <path d="M778.46 1414.4H595.17V710.23c-66.97 63.83-145.89 111.04-236.78 141.63V682.3c47.83-15.96 99.8-46.21 155.89-90.76 56.1-44.55 94.58-96.53 115.45-155.93h148.72v978.79zm244.16-533.83c-47.4-20.04-81.86-47.59-103.39-82.66-21.53-35.07-32.29-73.51-32.29-115.32 0-71.44 24.9-130.46 74.69-177.07 49.79-46.61 120.56-69.91 212.32-69.91s161.44 23.3 211.66 69.91 75.34 105.63 75.34 177.07c0 44.43-11.52 83.96-34.57 118.59-23.05 34.63-55.44 61.09-97.19 79.39 53.05 21.34 93.38 52.49 121 93.44 27.61 40.95 41.42 88.21 41.42 141.79 0 88.43-28.16 160.3-84.47 215.62-56.31 55.32-131.22 82.98-224.71 82.98-86.97 0-159.37-22.87-217.21-68.61-68.27-54.02-102.41-128.07-102.41-222.16 0-51.84 12.83-99.43 38.48-142.77 25.66-43.34 66.1-76.78 121.32-100.3zm37.83-184.91c0 36.59 10.33 65.12 30.98 85.59 20.66 20.47 48.16 30.71 82.51 30.71s62.62-10.35 83.49-31.04c20.87-20.69 31.31-49.33 31.31-85.92 0-34.41-10.33-61.96-30.98-82.66-20.66-20.69-47.94-31.04-81.86-31.04s-63.27 10.45-84.14 31.36c-20.87 20.91-31.31 48.57-31.31 82.98zm-16.96 410.33c0 50.53 12.94 89.95 38.81 118.27 25.87 28.31 58.16 42.47 96.86 42.47s69.14-13.61 93.93-40.84c24.79-27.23 37.18-66.54 37.18-117.94 0-44.87-12.61-80.91-37.83-108.14-25.22-27.22-57.18-40.84-95.89-40.84-44.79 0-78.17 15.46-100.12 46.39-21.96 30.93-32.94 64.47-32.94 100.62z" />
                        <path
                          fill="none"
                          stroke="currentColor"
                          stroke-linecap="round"
                          stroke-linejoin="round"
                          stroke-width="150"
                          d="M925 75c469.13 0 850 380.87 850 850s-380.87 850-850 850S75 1394.13 75 925 455.87 75 925 75h0z"
                        />
                        <path
                          fill="currentColor"
                          fill-rule="evenodd"
                          d="M400.96 299.44 299.44 400.96l1149.6 1149.6 101.52-101.52-1149.6-1149.6z"
                        />
                      </g>
                    </svg>
                  )}
                </div>
              )}
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-2">
                <CardTitle
                  className={`${isCompact ? "text-sm" : "text-lg"} truncate`}
                >
                  {extension.name}
                </CardTitle>
              </div>
              <CardDescription
                className={`${isCompact ? "text-xs" : "text-xs"} flex items-center truncate`}
              >
                <span className="text-muted-foreground">
                  v{extension.versionName}
                  &nbsp;&nbsp;
                  <ReactCountryFlag
                    countryCode={countryCode}
                    svg
                    style={{
                      width: isCompact ? "14px" : "14px",
                      height: isCompact ? "10px" : "10px",
                      marginBottom: isCompact ? "2px" : "2px",
                    }}
                    title={`${extension.lang.toUpperCase()} (${countryCode})`}
                  />
                </span>
              </CardDescription>
            </div>
          </div>
          <div className="flex flex-shrink-0 items-center gap-2">
            {extension.installed && (
              <ProviderSettingsButton
                variant={isCompact ? "default" : undefined}
                apkName={extension.apkName}
                providerName={extension.name}
                size="sm"
              />
            )}
            <Button
              variant={extension.installed ? "destructive" : "default"}
              size="sm"
              onClick={handleAction}
              disabled={isLoading}
              className="gap-2"
            >
              {isLoading ? (
                "..."
              ) : extension.installed ? (
                <>
                  <Trash2 className="h-4 w-4" />
                  {isCompact ? "Remove" : "Uninstall"}
                </>
              ) : (
                <>
                  <Download className="h-4 w-4" />
                  Install
                </>
              )}
            </Button>
          </div>
        </div>
      </CardHeader>
    </Card>
  );
}

interface ProviderManagerProps {
  /** Search functionality from useSearch hook or similar */
  searchTerm: string;
  setSearchTerm: (term: string) => void;
  clearSearch?: () => void;
  /** Layout configuration */
  isCompact?: boolean;
  showSearch?: boolean;
  showNsfwIndicator?: boolean;
  /** Grid configuration */
  installedGridCols?: string;
  availableGridCols?: string;
  /** Height constraints */
  installedMaxHeight?: string;
  availableMaxHeight?: string;
  /** Section titles */
  installedTitle?: string;
  availableTitle?: string;
  /** Description text */
  description?: React.ReactNode;
  /** Error handling */
  onError?: (error: string | null) => void;
  /** Loading state callback */
  onLoadingChange?: (loading: boolean) => void;
  /** Extensions change callback */
  onExtensionsChange?: (extensions: Provider[]) => void;
}

export function ProviderManager({
  searchTerm,
  setSearchTerm,
  clearSearch,
  isCompact = false,
  showSearch = true,
  showNsfwIndicator = true,
  installedGridCols = "grid-cols-1 sm:grid-cols-1 md:grid-cols-1 lg:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3",
  availableGridCols = "grid-cols-1 sm:grid-cols-1 md:grid-cols-1 lg:grid-cols-2 xl:grid-cols-2 2xl:grid-cols-3",
  installedMaxHeight = "",
  availableMaxHeight = "",
  installedTitle = "Installed",
  availableTitle = "Available",
  description,
  onError,
  onLoadingChange,
  onExtensionsChange,
}: ProviderManagerProps) {
  const [extensions, setExtensions] = useState<Provider[]>([]);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState<string | null>(null);
  const [showPreferencesFor, setShowPreferencesFor] = useState<{
    apkName: string;
    name: string;
  } | null>(null);
  const installedContainerRef = useRef<HTMLDivElement>(null);
  const availableContainerRef = useRef<HTMLDivElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [hasInstalledScrollbar, setHasInstalledScrollbar] = useState(false);
  const [hasAvailableScrollbar, setHasAvailableScrollbar] = useState(false);
  const [isUploadingApk, setIsUploadingApk] = useState(false);
  const [hideNsfwProviders, setHideNsfwProviders] = useState(true);
  const [filteredLanguages, setFilteredLanguages] = useState<string[] | null>(
    null,
  );
  const { data: settings } = useSettings();

  useEffect(() => {
    const loadExtensions = async () => {
      try {
        setLoading(true);
        onLoadingChange?.(true);
        const data = await providerService.getProviders();
        setExtensions(data);
        onExtensionsChange?.(data);
      } catch (error) {
        console.error("Failed to load extensions:", error);
        onError?.("Failed to load sources. Please try again.");
      } finally {
        setLoading(false);
        onLoadingChange?.(false);
      }
    };
    void loadExtensions();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []); // Empty dependency array - only run on mount

  // Notify parent component when extensions change
  useEffect(() => {
    onExtensionsChange?.(extensions);
  }, [extensions, onExtensionsChange]);

  // Derive language options from available (non-installed) providers
  const availableLanguageOptions = useMemo(() => {
    const langs = [
      ...new Set(
        extensions
          .filter((ext) => !ext.installed)
          .map((ext) => ext.lang)
          .filter((lang) => lang !== "all"),
      ),
    ].sort();
    return langs.map((lang) => {
      let displayName = lang;
      try {
        displayName =
          new Intl.DisplayNames(["en"], { type: "language" }).of(lang) ?? lang;
      } catch {
        // Fall back to raw code for unrecognized codes
      }
      return { value: lang, label: displayName };
    });
  }, [extensions]);

  // Initialise language filter from user's preferred languages
  useEffect(() => {
    if (filteredLanguages !== null) return;
    if (availableLanguageOptions.length === 0) return;

    const availableLangs = new Set(
      availableLanguageOptions.map((o) => o.value),
    );

    if (settings?.preferredLanguages?.length) {
      const validPreferred = settings.preferredLanguages.filter((lang) =>
        availableLangs.has(lang),
      );
      if (validPreferred.length > 0) {
        setFilteredLanguages(validPreferred);
        return;
      }
    }
    // No preferred languages or none match available — default to all
    setFilteredLanguages([...availableLangs]);
  }, [settings, availableLanguageOptions, filteredLanguages]);

  // Installed extensions should not be filtered by search term
  const installedExtensions = useMemo(
    () => extensions.filter((ext) => ext.installed),
    [extensions],
  );

  // Filter available extensions based on search term, NSFW, and language
  const availableExtensions = useMemo(() => {
    let available = extensions.filter((ext) => !ext.installed);
    if (hideNsfwProviders) {
      available = available.filter((ext) => !ext.isNsfw);
    }
    if (filteredLanguages !== null) {
      available = available.filter(
        (ext) => ext.lang === "all" || filteredLanguages.includes(ext.lang),
      );
    }

    if (!searchTerm.trim()) {
      return available;
    }
    const search = searchTerm.toLowerCase();
    return available.filter(
      (ext) =>
        ext.name.toLowerCase().includes(search) ||
        ext.lang.toLowerCase().includes(search),
    );
  }, [extensions, searchTerm, hideNsfwProviders, filteredLanguages]);

  const availableTotalCount = extensions.filter((ext) => !ext.installed).length;

  const handleInstall = async (pkgName: string) => {
    try {
      setActionLoading(pkgName);
      onError?.(null);
      await providerService.installProvider(pkgName);

      // Update local state optimistically instead of full refresh
      setExtensions((prevExtensions) =>
        prevExtensions.map((ext) =>
          ext.pkgName === pkgName ? { ...ext, installed: true } : ext,
        ),
      );
      clearSearch?.(); // Clear search after successful installation

      // Find the installed extension to get its details for preferences
      const installedExtension = extensions.find(
        (ext) => ext.pkgName === pkgName,
      );
      if (installedExtension) {
        setShowPreferencesFor({
          apkName: installedExtension.apkName,
          name: installedExtension.name,
        });
      }
    } catch (error) {
      console.error("Failed to install extension:", error);
      onError?.("Failed to install extension. Please try again.");
    } finally {
      setActionLoading(null);
    }
  };

  const handleUninstall = async (pkgName: string) => {
    try {
      setActionLoading(pkgName);
      onError?.(null);
      await providerService.uninstallProvider(pkgName);

      // Update local state optimistically instead of full refresh
      setExtensions((prevExtensions) =>
        prevExtensions.map((ext) =>
          ext.pkgName === pkgName ? { ...ext, installed: false } : ext,
        ),
      );
      clearSearch?.(); // Clear search after successful uninstallation
    } catch (error) {
      console.error("Failed to uninstall extension:", error);
      onError?.("Failed to uninstall extension. Please try again.");
    } finally {
      setActionLoading(null);
    }
  };

  const handleInstallFromApk = async (file: File) => {
    try {
      setIsUploadingApk(true);
      onError?.(null);

      // Install the APK file and get the pkgName
      const pkgName = await providerService.installProviderFromFile(file);

      if (pkgName) {
        // Refresh the extensions list to get the newly installed provider
        const updatedExtensions = await providerService.getProviders();
        setExtensions(updatedExtensions);
        onExtensionsChange?.(updatedExtensions);

        // Find the newly installed extension by pkgName (same as normal install)
        const newExtension = updatedExtensions.find(
          (ext) => ext.pkgName === pkgName,
        );
        if (newExtension) {
          // Open preference requester for the newly installed provider
          setShowPreferencesFor({
            apkName: newExtension.apkName,
            name: newExtension.name,
          });
        }
      }
    } catch (error) {
      console.error("Failed to install APK:", error);
      onError?.(
        "Failed to install APK file. Please check the file and try again.",
      );
    } finally {
      setIsUploadingApk(false);
    }
  };

  const handleApkButtonClick = () => {
    fileInputRef.current?.click();
  };

  const handleFileChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file && file.name.endsWith(".apk")) {
      handleInstallFromApk(file);
    } else {
      onError?.("Please select a valid APK file.");
    }
    // Reset the input so the same file can be selected again
    event.target.value = "";
  };

  useEffect(() => {
    const checkScrollbars = () => {
      if (installedContainerRef.current) {
        const { scrollHeight, clientHeight } = installedContainerRef.current;
        setHasInstalledScrollbar(scrollHeight > clientHeight);
      }
      if (availableContainerRef.current) {
        const { scrollHeight, clientHeight } = availableContainerRef.current;
        setHasAvailableScrollbar(scrollHeight > clientHeight);
      }
    };

    // Check on mount and when extensions change
    checkScrollbars();

    // Also check on window resize
    window.addEventListener("resize", checkScrollbars);
    return () => window.removeEventListener("resize", checkScrollbars);
  }, [installedExtensions, availableExtensions]); // Re-check when content changes

  if (loading) {
    return (
      <div className="flex min-h-[200px] items-center justify-center">
        <div className="text-muted-foreground">Loading sources...</div>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {(showSearch || description) && (
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
          {description && (
            <div className="text-muted-foreground text-sm">{description}</div>
          )}
          {showSearch && (
            <div className="relative w-full flex-shrink-0 sm:w-80">
              <Search className="text-muted-foreground absolute top-2.5 left-2.5 h-4 w-4" />
              <Input
                type="search"
                placeholder="Search sources..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="pl-8"
              />
            </div>
          )}
        </div>
      )}

      {installedExtensions.length > 0 && (
        <div className="space-y-4">
          <div className="flex items-center justify-between">
            <h2
              className={`${isCompact ? "text-lg font-medium" : "text-xl font-semibold"}`}
            >
              {installedTitle}
            </h2>
            <p className="text-muted-foreground text-sm">
              {installedExtensions.length} provider
              {installedExtensions.length !== 1 ? "s" : ""} installed
            </p>
          </div>
          <div
            ref={installedContainerRef}
            className={`grid ${installedGridCols} gap-${isCompact ? "2" : "4"} ${installedMaxHeight ? `${installedMaxHeight} overflow-y-auto` : ""} ${hasInstalledScrollbar ? "pr-2" : ""}`}
          >
            {installedExtensions.map((extension) => (
              <ProviderCard
                key={extension.pkgName}
                extension={extension}
                onUninstall={handleUninstall}
                isLoading={actionLoading === extension.pkgName}
                isCompact={isCompact}
                showNsfwIndicator={showNsfwIndicator}
              />
            ))}
          </div>
        </div>
      )}

      {installedExtensions.length > 0 && availableExtensions.length > 0 && (
        <Separator />
      )}

      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <h2
            className={`${isCompact ? "text-lg font-medium" : "text-xl font-semibold"}`}
          >
            {availableTitle}
          </h2>
          <div className="flex items-center gap-4">
            <div className="flex items-center gap-1.5">
              <Checkbox
                id="hide-nsfw"
                checked={hideNsfwProviders}
                onCheckedChange={(checked) => {
                  setHideNsfwProviders(!!checked);
                }}
              />
              <Label
                htmlFor="hide-nsfw"
                className="text-muted-foreground cursor-pointer text-sm"
              >
                Hide NSFW
              </Label>
            </div>
            <div className="w-48">
              <MultiSelectLanguages
                options={availableLanguageOptions}
                selectedValues={filteredLanguages ?? []}
                onSelectionChange={setFilteredLanguages}
              />
            </div>
            <p className="text-muted-foreground text-sm">
              {availableTotalCount} provider
              {availableTotalCount !== 1 ? "s" : ""} available
            </p>
            <Button
              onClick={handleApkButtonClick}
              disabled={isUploadingApk}
              variant="default"
              size="sm"
              className="gap-2"
            >
              <Upload className="h-4 w-4" />
              {isUploadingApk ? "Installing..." : "Install From APK"}
            </Button>
          </div>
        </div>
        {availableExtensions.length > 0 && (
          <div
            ref={availableContainerRef}
            className={`grid ${availableGridCols} gap-${isCompact ? "2" : "4"} ${availableMaxHeight ? `${availableMaxHeight} overflow-y-auto` : ""} ${hasAvailableScrollbar ? "pr-2" : ""}`}
          >
            {availableExtensions.map((extension) => (
              <ProviderCard
                key={extension.pkgName}
                extension={extension}
                onInstall={handleInstall}
                isLoading={actionLoading === extension.pkgName}
                isCompact={isCompact}
                showNsfwIndicator={showNsfwIndicator}
              />
            ))}
          </div>
        )}
      </div>

      {installedExtensions.length === 0 && availableExtensions.length === 0 && (
        <div className="text-muted-foreground py-8 text-center">
          {searchTerm.trim() ? (
            <>
              No sources found matching &ldquo;{searchTerm}&rdquo;.
              <br />
              Try adjusting your search or{" "}
              <button
                onClick={() => setSearchTerm("")}
                className="text-primary underline hover:no-underline"
              >
                view all sources
              </button>
              .
            </>
          ) : (
            "No sources available."
          )}
        </div>
      )}

      {/* Hidden file input for APK upload */}
      <input
        ref={fileInputRef}
        type="file"
        accept=".apk"
        style={{ display: "none" }}
        onChange={handleFileChange}
      />

      {/* Auto-open preferences after installation */}
      {showPreferencesFor && (
        <ProviderPreferencesRequester
          open={true}
          onOpenChange={(open) => !open && setShowPreferencesFor(null)}
          apkName={showPreferencesFor.apkName}
          providerName={showPreferencesFor.name}
        />
      )}
    </div>
  );
}
