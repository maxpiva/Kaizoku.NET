"use client";

import React, { useState, useRef } from "react";
import { Button } from "@/components/ui/button";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Checkbox } from "@/components/ui/checkbox";
import { useAuth } from "@/contexts/auth-context";
import { useMutation } from "@tanstack/react-query";
import { userService } from "@/lib/api/services/userService";
import { UserIcon, Upload } from "lucide-react";
import { fetchGravatarBase64 } from "@/lib/gravatar";
import { Badge } from "@/components/ui/badge";
import { Plus, X, Save, Loader2, GripVertical, BadgeCheck, AlertCircle } from "lucide-react";
import { useRouter } from "next/navigation";
import {
  useSettings,
  useAvailableLanguages,
  useUpdateSettings,
  useVerifyContributor,
} from "@/lib/api/hooks/useSettings";
import { type Settings, NsfwVisibility } from "@/lib/api/types";
import { useToast } from "@/hooks/use-toast";
import ReactCountryFlag from "react-country-flag";
import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from "@dnd-kit/core";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { RadioGroup, RadioGroupItem } from "../ui/radio-group";

// Helper functions
const isValidUrl = (url: string): boolean => {
  try {
    new URL(url);
    return true;
  } catch {
    return false;
  }
};

/**
 * Re-hosts a set-password URL onto the CURRENT origin.
 *
 * The backend builds setPasswordUrl from the configured ExternalDomain, falling
 * back to the request host. But if ExternalDomain is empty/stale, it could be an
 * absolute URL pointing at another host (e.g. http://localhost:9833) while the
 * user is browsing via a LAN IP (e.g. http://192.168.68.70:9833). Navigating
 * there would send the username/token to the wrong host and appear to "bounce
 * back to login".
 *
 * We keep only the path + query (which carry the token) and prefix them with the
 * current origin so the redirect can never leave the page the user is on.
 */
const safeSetPasswordUrl = (url: string): string => {
  try {
    const parsed = new URL(url, window.location.href);
    return `${window.location.origin}${parsed.pathname}${parsed.search}`;
  } catch {
    return url;
  }
};

const timeSpanToTimeInput = (timeSpan: string): string => {
  if (!timeSpan) return "00:00";

  const parts = timeSpan.split(".");
  let timePart = timeSpan;

  if (parts.length === 2 && parts[1]) {
    timePart = parts[1];
  }

  const [hours = 0, minutes = 0] = timePart
    .split(":")
    .map((p) => parseInt(p) || 0);

  const paddedHours = hours.toString().padStart(2, "0");
  const paddedMinutes = minutes.toString().padStart(2, "0");

  return `${paddedHours}:${paddedMinutes}`;
};

const timeSpanToTimeInputSeconds = (timeSpan: string): string => {
  if (!timeSpan) return "00:00:00";

  const parts = timeSpan.split(".");
  let timePart = timeSpan;

  if (parts.length === 2 && parts[1]) {
    timePart = parts[1];
  }

  const [hours = 0, minutes = 0, seconds = 0] = timePart
    .split(":")
    .map((p) => parseInt(p) || 0);

  const paddedHours = hours.toString().padStart(2, "0");
  const paddedMinutes = minutes.toString().padStart(2, "0");
  const paddedSeconds = seconds.toString().padStart(2, "0");
  return `${paddedHours}:${paddedMinutes}:${paddedSeconds}`;
};

const timeInputToTimeSpan = (timeInput: string): string => {
  if (!timeInput) return "00:00:00";

  const [hours = 0, minutes = 0] = timeInput
    .split(":")
    .map((p) => parseInt(p) || 0);

  const paddedHours = hours.toString().padStart(2, "0");
  const paddedMinutes = minutes.toString().padStart(2, "0");

  return `${paddedHours}:${paddedMinutes}:00`;
};

const timeInputToTimeSpanSeconds = (timeInput: string): string => {
  if (!timeInput) return "00:00:00";

  const [hours = 0, minutes = 0, seconds = 0] = timeInput
    .split(":")
    .map((p) => parseInt(p) || 0);

  const paddedHours = hours.toString().padStart(2, "0");
  const paddedMinutes = minutes.toString().padStart(2, "0");
  const paddedSeconds = seconds.toString().padStart(2, "0");

  return `${paddedHours}:${paddedMinutes}:${paddedSeconds}`;
};

// Sortable Language Badge Component
function SortableLanguageBadge({
  language,
  onRemove,
}: {
  language: string;
  onRemove: (language: string) => void;
}) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: language });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  const countryCode = getCountryCodeForLanguage(language);

  return (
    <div ref={setNodeRef} style={style} className="inline-flex" {...attributes}>
      <Badge
        variant="secondary"
        className="flex items-center gap-1 select-none"
      >
        <div className="flex cursor-move items-center" {...listeners}>
          <GripVertical className="text-muted-foreground h-3 w-3" />
        </div>
        <ReactCountryFlag
          countryCode={countryCode}
          svg
          style={{
            width: "14px",
            height: "14px",
          }}
          title={`${language} (${countryCode})`}
        />
        <span className="mx-1">{language}</span>
        <button
          type="button"
          className="hover:text-destructive flex h-3 w-3 cursor-pointer items-center justify-center"
          onClick={(e) => {
            e.preventDefault();
            e.stopPropagation();
            onRemove(language);
          }}
          onPointerDown={(e) => {
            e.stopPropagation();
          }}
        >
          <X className="h-3 w-3" />
        </button>
      </Badge>
    </div>
  );
}

// Settings section configuration
interface SettingsSection {
  id: string;
  title: string;
  description: string;
  component: React.ComponentType<{
    localSettings: Settings;
    setLocalSettings: (updater: (prev: Settings) => Settings) => void;
  }>;
}

// Content Preferences Section
function ContentPreferencesSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const { data: availableLanguages = [] } = useAvailableLanguages();
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  const handleDragEnd = React.useCallback(
    (event: DragEndEvent) => {
      const { active, over } = event;
      if (active.id !== over?.id) {
        setLocalSettings((prev) => {
          if (!prev) return prev;
          const oldIndex = (prev.preferredLanguages || []).indexOf(
            active.id as string,
          );
          const newIndex = (prev.preferredLanguages || []).indexOf(
            over?.id as string,
          );
          return {
            ...prev,
            preferredLanguages: arrayMove(
              prev.preferredLanguages || [],
              oldIndex,
              newIndex,
            ),
          };
        });
      }
    },
    [setLocalSettings],
  );

  const addLanguage = React.useCallback(
    (language: string) => {
      setLocalSettings((prev) => {
        if (
          !prev ||
          !language ||
          (prev.preferredLanguages || []).includes(language)
        )
          return prev;
        return {
          ...prev,
          preferredLanguages: [...(prev.preferredLanguages || []), language],
        };
      });
    },
    [setLocalSettings],
  );

  const removeLanguage = React.useCallback(
    (language: string) => {
      setLocalSettings((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          preferredLanguages: (prev.preferredLanguages || []).filter(
            (lang) => lang !== language,
          ),
        };
      });
    },
    [setLocalSettings],
  );

  const availableLanguagesToAdd = React.useMemo(
    () =>
      availableLanguages.filter(
        (lang) => !(localSettings.preferredLanguages || []).includes(lang),
      ),
    [availableLanguages, localSettings.preferredLanguages],
  );

  return (
    <CardContent className="space-y-6">
      <div className="space-y-4">
        <h4 className="text-sm font-semibold">Language</h4>
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={handleDragEnd}
        >
          <SortableContext
            items={localSettings.preferredLanguages || []}
            strategy={verticalListSortingStrategy}
          >
            <div className="flex flex-wrap gap-2">
              {(localSettings.preferredLanguages || []).map((language) => (
                <SortableLanguageBadge
                  key={language}
                  language={language}
                  onRemove={removeLanguage}
                />
              ))}
            </div>
          </SortableContext>
        </DndContext>

        {availableLanguagesToAdd.length > 0 && (
          <div className="space-y-2">
            <Label className="text-sm font-medium">
              Available languages (Derived from your sources):
            </Label>
            <div className="flex max-h-40 flex-wrap gap-1 overflow-y-auto">
              {availableLanguagesToAdd.map((language) => {
                const countryCode = getCountryCodeForLanguage(language);
                return (
                  <Badge
                    key={language}
                    variant="outline"
                    className="hover:bg-primary hover:text-primary-foreground flex cursor-pointer items-center gap-1"
                    onClick={() => addLanguage(language)}
                  >
                    <ReactCountryFlag
                      countryCode={countryCode}
                      svg
                      style={{
                        width: "12px",
                        height: "12px",
                      }}
                      title={`${language} (${countryCode})`}
                    />
                    {language}
                  </Badge>
                );
              })}
            </div>
          </div>
        )}
      </div>

      <div className="space-y-4">
        <h4 className="text-sm font-semibold">NSFW</h4>
        <RadioGroup
          value={localSettings.nsfwVisibility}
          onValueChange={(value) => {
            setLocalSettings((prev) => ({
              ...prev,
              nsfwVisibility: value as NsfwVisibility,
            }));
          }}
          className="space-y-2"
        >
          <div className="flex items-center gap-2">
            <RadioGroupItem value={NsfwVisibility.AlwaysHide} id="nsfw-always-hide" />
            <Label htmlFor="nsfw-always-hide" className="cursor-pointer text-sm">
              Always hide
            </Label>
            <span className="text-muted-foreground text-xs">
              NSFW sources are never shown
            </span>
          </div>
          <div className="flex items-center gap-2">
            <RadioGroupItem value={NsfwVisibility.HideByDefault} id="nsfw-hide-default" />
            <Label htmlFor="nsfw-hide-default" className="cursor-pointer text-sm">
              Hide by default
            </Label>
            <span className="text-muted-foreground text-xs">
              Hidden by default, but can be toggled in source lists
            </span>
          </div>
          <div className="flex items-center gap-2">
            <RadioGroupItem value={NsfwVisibility.Show} id="nsfw-show" />
            <Label htmlFor="nsfw-show" className="cursor-pointer text-sm">
              Show
            </Label>
            <span className="text-muted-foreground text-xs">
              NSFW sources are always visible
            </span>
          </div>
        </RadioGroup>
      </div>
    </CardContent>
  );
}

// Mihon Repositories Section
function MihonRepositoriesSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const [newRepository, setNewRepository] = useState("");
  const addRepository = React.useCallback(() => {
    if (!newRepository || !isValidUrl(newRepository)) return;
    setLocalSettings((prev) => {
      if (!prev || (prev.mihonRepositories || []).includes(newRepository))
        return prev;
      return {
        ...prev,
        mihonRepositories: [...(prev.mihonRepositories || []), newRepository],
      };
    });
    setNewRepository("");
  }, [newRepository, setLocalSettings]);

  const removeRepository = React.useCallback(
    (repository: string) => {
      setLocalSettings((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          mihonRepositories: (prev.mihonRepositories || []).filter(
            (repo) => repo !== repository,
          ),
        };
      });
    },
    [setLocalSettings],
  );

  return (
    <CardContent className="space-y-4">
      <div className="space-y-2">
        {(localSettings.mihonRepositories || []).map((repository, index) => (
          <div key={index} className="flex items-center gap-2">
            <Input value={repository} readOnly className="flex-1" />
            <Button
              variant="outline"
              size="sm"
              onClick={() => removeRepository(repository)}
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
        ))}
      </div>
      <div className="flex items-center gap-2">
        <Input
          placeholder="Enter repository URL"
          value={newRepository}
          onChange={(e) => setNewRepository(e.target.value)}
          className="flex-1"
        />
        <Button
          onClick={addRepository}
          disabled={!newRepository || !isValidUrl(newRepository)}
        >
          <Plus className="h-4 w-4" />
        </Button>
      </div>
    </CardContent>
  );
}

// Download Settings Section
function DownloadSettingsSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  return (
    <CardContent className="space-y-4">
      <div className="grid gap-4 md:grid-cols-2">
        {" "}
        <div>
          <Label htmlFor="simultaneous-downloads">
            Number of Simultaneous Downloads
          </Label>
          <Input
            id="simultaneous-downloads"
            type="number"
            min="1"
            max="20"
            value={localSettings.numberOfSimultaneousDownloads}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                numberOfSimultaneousDownloads: parseInt(e.target.value) || 1,
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            Maximum number of downloads that can run simultaneously
          </p>
        </div>
        <div>
          <Label htmlFor="simultaneous-downloads-per-provider">
            Downloads Per Source
          </Label>
          <Input
            id="simultaneous-downloads-per-provider"
            type="number"
            min="1"
            max="10"
            value={localSettings.numberOfSimultaneousDownloadsPerProvider}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                numberOfSimultaneousDownloadsPerProvider:
                  parseInt(e.target.value) || 1,
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            Maximum number of simultaneous downloads per source
          </p>
        </div>
        <div>
          <Label htmlFor="simultaneous-searches">
            Number of Simultaneous Searches
          </Label>
          <Input
            id="simultaneous-searches"
            type="number"
            min="1"
            max="20"
            value={localSettings.numberOfSimultaneousSearches}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                numberOfSimultaneousSearches: parseInt(e.target.value) || 1,
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            Maximum number of searches that can run simultaneously
          </p>
        </div>
        <div>
          <Label htmlFor="download-retry-time">
            Chapter Download Retry Time
          </Label>
          <Input
            id="download-retry-time"
            type="text"
            placeholder="HH:MM"
            pattern="^([01]\\d|2[0-3]):[0-5]\\d$"
            value={timeSpanToTimeInput(
              localSettings.chapterDownloadFailRetryTime,
            )}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                chapterDownloadFailRetryTime: timeInputToTimeSpan(
                  e.target.value,
                ),
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            How long to wait before retrying a failed chapter download
          </p>
        </div>
        <div>
          <Label htmlFor="download-retries">Chapter Download Max Retries</Label>
          <Input
            id="download-retries"
            type="number"
            min="0"
            max="1000"
            value={localSettings.chapterDownloadFailRetries}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                chapterDownloadFailRetries: parseInt(e.target.value) || 0,
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            Maximum number of retry attempts for failed chapter downloads
          </p>
        </div>
      </div>
    </CardContent>
  );
}

// Schedule Tasks Section
function ScheduleTasksSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  return (
    <CardContent className="space-y-4">
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-2">
        <div>
          <Label htmlFor="per-title-update">Per Title Update Schedule</Label>
          <Input
            lang="en-GB"
            id="per-title-update"
            type="text"
            placeholder="HH:MM"
            pattern="^([01]\\d|2[0-3]):[0-5]\\d$"
            required
            value={timeSpanToTimeInput(localSettings.perTitleUpdateSchedule)}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                perTitleUpdateSchedule: timeInputToTimeSpan(e.target.value),
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            How often to check for updates per title
          </p>
        </div>

        <div>
          <Label htmlFor="per-source-update">Per Source Update Schedule</Label>
          <Input
            id="per-source-update"
            type="text"
            placeholder="HH:MM"
            pattern="^([01]\\d|2[0-3]):[0-5]\\d$"
            required
            value={timeSpanToTimeInput(localSettings.perSourceUpdateSchedule)}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                perSourceUpdateSchedule: timeInputToTimeSpan(e.target.value),
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            How often to check for updates per source
          </p>
        </div>

        <div>
          <Label htmlFor="extensions-check">
            Extensions Update Check Schedule
          </Label>
          <Input
            id="extensions-check"
            type="text"
            placeholder="HH:MM"
            pattern="^([01]\\d|2[0-3]):[0-5]\\d$"
            required
            value={timeSpanToTimeInput(
              localSettings.extensionsCheckForUpdateSchedule,
            )}
            onChange={(e) =>
              setLocalSettings((prev) => ({
                ...prev,
                extensionsCheckForUpdateSchedule: timeInputToTimeSpan(
                  e.target.value,
                ),
              }))
            }
          />
          <p className="text-muted-foreground mt-1 text-sm">
            How often to check for extension updates
          </p>
        </div>
      </div>
    </CardContent>
  );
}

// Storage Section
function StorageSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const [newCategory, setNewCategory] = useState("");

  const addCategory = React.useCallback(() => {
    if (!newCategory) return;
    setLocalSettings((prev) => {
      if (!prev || (prev.categories || []).includes(newCategory)) return prev;
      return {
        ...prev,
        categories: [...(prev.categories || []), newCategory],
      };
    });
    setNewCategory("");
  }, [newCategory, setLocalSettings]);

  const removeCategory = React.useCallback(
    (category: string) => {
      setLocalSettings((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          categories: (prev.categories || []).filter((cat) => cat !== category),
        };
      });
    },
    [setLocalSettings],
  );

  return (
    <CardContent className="space-y-4">
      <div>
        <Label htmlFor="storage-folder">Storage Folder</Label>
        <Input
          id="storage-folder"
          value={localSettings.storageFolder || ""}
          readOnly
          className="bg-muted"
        />
        <p className="text-muted-foreground mt-1 text-sm">
          Current folder where series archives are stored
        </p>
      </div>

      <div className="rounded-lg border bg-muted/20 p-4 space-y-3">
        <div>
          <Label htmlFor="chapter-filename-template">Chapter filename template</Label>
          <p id="chapter-filename-help" className="text-muted-foreground mt-1 text-sm">
            Leave empty for canonical names. Applies to new downloads only; .cbz and a
            source/language identity suffix are always added automatically.
          </p>
        </div>
        <Input
          id="chapter-filename-template"
          value={localSettings.chapterFilenameTemplate ?? ""}
          maxLength={160}
          placeholder="Canonical naming (default)"
          aria-describedby="chapter-filename-help chapter-filename-tokens chapter-filename-recovery"
          onChange={(e) => setLocalSettings((prev) => ({ ...prev, chapterFilenameTemplate: e.target.value }))}
          className="font-mono text-sm"
        />
        <p id="chapter-filename-tokens" className="text-muted-foreground text-sm break-words">
          Tokens: {"{series}, {chapter}, {chapter:0000}, {title}, {source}, {language}"}.
          Include a chapter token. Zero padding keeps fractional chapters (e.g. 0012.5).
          No paths, dots, extensions, or other tokens. Example: {"Capítulo {chapter:0000}"}.
        </p>
        {!!localSettings.chapterFilenameTemplate?.trim() && (
          <p id="chapter-filename-recovery" role="note" className="text-sm border-l-2 border-primary pl-3">
            Keep rensaio.json with your series: custom filenames cannot guarantee filename-only
            recovery. ComicInfo metadata is preserved. The existing Rename action skips custom archive names.
          </p>
        )}
      </div>

      <div className="rounded-lg border bg-muted/20 p-4 space-y-3">
        <div className="flex items-center gap-3">
          <Switch
            id="inject-series-cover"
            checked={localSettings.injectSeriesCover ?? false}
            aria-describedby="inject-series-cover-help"
            onCheckedChange={(checked) => setLocalSettings((prev) => ({ ...prev, injectSeriesCover: checked }))}
          />
          <Label htmlFor="inject-series-cover">Include series cover in new CBZ files</Label>
        </div>
        <p id="inject-series-cover-help" className="text-muted-foreground text-sm">
          Adds the existing series cover.jpg as the first image and marks it as FrontCover
          in ComicInfo. Choose the cover source in the series settings. Off by default.
        </p>
        <p className="text-muted-foreground text-sm">
          New downloads only, not existing archives. Uses the already saved JPEG (up to 5 MiB,
          8192 pixels per side, 32 megapixels), with no extra web requests. Missing or invalid
          covers are skipped. Each CBZ contains its own copy, so storage usage increases.
          No chapter-specific cover editor or RSS/web scraper is included.
        </p>
      </div>

      <div className="flex items-center space-x-2">
        <Switch
          id="categorized-folders"
          checked={localSettings.categorizedFolders}
          onCheckedChange={(checked) =>
            setLocalSettings((prev) => ({
              ...prev,
              categorizedFolders: checked,
            }))
          }
        />
        <Label htmlFor="categorized-folders">Enable Categorized Folders</Label>
      </div>
      {localSettings.categorizedFolders && (
        <div className="space-y-4">
          <div>
            <Label>Categories</Label>
            <p className="text-muted-foreground mb-2 text-sm">
              Define categories for organizing series. Category will be
              selectable when adding series.
            </p>
          </div>
          <div className="flex flex-wrap gap-2">
            {(localSettings.categories || []).map((category) => (
              <Badge
                key={category}
                variant="secondary"
                className="flex items-center gap-1"
              >
                {category}
                <X
                  className="hover:text-destructive h-3 w-3 cursor-pointer"
                  onClick={() => removeCategory(category)}
                />
              </Badge>
            ))}
          </div>
          <div className="flex items-center gap-2">
            <Input
              placeholder="Enter category name"
              value={newCategory}
              onChange={(e) => setNewCategory(e.target.value)}
              className="flex-1"
            />
            <Button onClick={addCategory} disabled={!newCategory}>
              <Plus className="h-4 w-4" />
            </Button>
          </div>
        </div>
      )}
    </CardContent>
  );
}

// FlareSolverr Section
function FlareSolverrSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  return (
    <CardContent className="space-y-4">
      <div className="flex items-center space-x-2">
        <Switch
          id="flaresolverr-enabled"
          checked={localSettings.flareSolverrEnabled}
          onCheckedChange={(checked) =>
            setLocalSettings((prev) => ({
              ...prev,
              flareSolverrEnabled: checked,
            }))
          }
        />
        <Label htmlFor="flaresolverr-enabled">Enable FlareSolverr</Label>
      </div>

      {localSettings.flareSolverrEnabled && (
        <div className="border-muted space-y-4 border-l-2 pl-6">
          <div>
            <Label htmlFor="flaresolverr-url">FlareSolverr URL</Label>
            <Input
              id="flaresolverr-url"
              value={localSettings.flareSolverrUrl}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  flareSolverrUrl: e.target.value,
                }))
              }
              placeholder="http://localhost:8191"
            />
          </div>

          <div>
            <Label htmlFor="flaresolverr-timeout">FlareSolverr Timeout</Label>
            <Input
              id="flaresolverr-timeout"
              type="text"
              placeholder="HH:MM:SS"
              pattern="^([01]\\d|2[0-3]):[0-5]\\d:[0-5]\\d$"
              required
              value={timeSpanToTimeInputSeconds(
                localSettings.flareSolverrTimeout,
              )}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  flareSolverrTimeout: timeInputToTimeSpanSeconds(
                    e.target.value,
                  ),
                }))
              }
            />
            <p className="text-muted-foreground mt-1 text-sm">
              Request timeout for FlareSolverr operations
            </p>
          </div>

          <div>
            <Label htmlFor="flaresolverr-session-ttl">Session TTL</Label>
            <Input
              id="flaresolverr-session-ttl"
              type="text"
              placeholder="HH:MM"
              pattern="^([01]\\d|2[0-3]):[0-5]\\d$"
              required
              value={timeSpanToTimeInput(localSettings.flareSolverrSessionTtl)}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  flareSolverrSessionTtl: timeInputToTimeSpan(e.target.value),
                }))
              }
            />
            <p className="text-muted-foreground mt-1 text-sm">
              How long FlareSolverr sessions should remain active
            </p>
          </div>

          <div className="flex items-center space-x-2">
            <Switch
              id="flaresolverr-fallback"
              checked={localSettings.flareSolverrAsResponseFallback}
              onCheckedChange={(checked) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  flareSolverrAsResponseFallback: checked,
                }))
              }
            />
            <Label htmlFor="flaresolverr-fallback">
              Use as Response Fallback
            </Label>
          </div>
        </div>
      )}
    </CardContent>
  );
}

// Socks Settings Section
function SocksSettingsSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const isEnabled = localSettings.socksProxyEnabled;

  return (
    <CardContent className="space-y-4">
      <div className="flex items-center space-x-2">
        <Switch
          id="socks-proxy-enabled"
          checked={localSettings.socksProxyEnabled}
          onCheckedChange={(checked) =>
            setLocalSettings((prev) => ({
              ...prev,
              socksProxyEnabled: checked,
            }))
          }
        />
        <Label htmlFor="socks-proxy-enabled">Enable SOCKS Proxy</Label>
      </div>

      <div className="border-muted space-y-4 border-l-2 pl-6">
        <div className="grid gap-4 md:grid-cols-2">
          <div>
            <Label htmlFor="socks-proxy-version">SOCKS Version</Label>
            <Input
              id="socks-proxy-version"
              type="number"
              min="4"
              max="5"
              value={localSettings.socksProxyVersion ?? 5}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  socksProxyVersion: Math.min(
                    5,
                    Math.max(4, parseInt(e.target.value) || 5),
                  ),
                }))
              }
              disabled={!isEnabled}
            />
          </div>
          <div>
            <Label htmlFor="socks-proxy-host">Host</Label>
            <Input
              id="socks-proxy-host"
              value={localSettings.socksProxyHost || ""}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  socksProxyHost: e.target.value,
                }))
              }
              placeholder="127.0.0.1"
              disabled={!isEnabled}
            />
          </div>
          <div>
            <Label htmlFor="socks-proxy-port">Port</Label>
            <Input
              id="socks-proxy-port"
              type="number"
              min="1"
              max="65535"
              value={localSettings.socksProxyPort ?? 0}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  socksProxyPort: Math.min(
                    65535,
                    Math.max(1, parseInt(e.target.value) || 0),
                  ),
                }))
              }
              disabled={!isEnabled}
            />
          </div>
          <div>
            <Label htmlFor="socks-proxy-username">Username</Label>
            <Input
              id="socks-proxy-username"
              value={localSettings.socksProxyUsername || ""}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  socksProxyUsername: e.target.value,
                }))
              }
              placeholder="Optional"
              disabled={!isEnabled}
            />
          </div>
          <div>
            <Label htmlFor="socks-proxy-password">Password</Label>
            <Input
              id="socks-proxy-password"
              type="password"
              value={localSettings.socksProxyPassword || ""}
              onChange={(e) =>
                setLocalSettings((prev) => ({
                  ...prev,
                  socksProxyPassword: e.target.value,
                }))
              }
              placeholder="Optional"
              disabled={!isEnabled}
            />
          </div>
        </div>
        <p className="text-muted-foreground text-sm">
          Configure a SOCKS4/5 proxy for provider requests.
        </p>
      </div>
    </CardContent>
  );
}

// Profile Settings Section
function ProfileSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const { user } = useAuth();
  const updateMutation = useMutation({
    mutationFn: (data: { avatarBase64?: string; avatarContentType?: string; removeAvatar?: boolean }) =>
      userService.updateMe(data),
  });
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [gravatarEmail, setGravatarEmail] = useState('');
  const [localError, setLocalError] = useState('');

  const avatarSrc = user?.avatarBase64
    ? `data:${user.avatarContentType || 'image/png'};base64,${user.avatarBase64}`
    : null;

  const handleFileUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    setLocalError('');
    if (file.size > 2 * 1024 * 1024) { setLocalError('Image must be less than 2MB'); return; }
    const reader = new FileReader();
    reader.onload = async () => {
      const base64 = (reader.result as string).split(',')[1];
      try {
        await updateMutation.mutateAsync({ avatarBase64: base64, avatarContentType: file.type });
      } catch { setLocalError('Failed to upload avatar'); }
    };
    reader.readAsDataURL(file);
  };

  const handleGravatar = async () => {
    if (!gravatarEmail.trim()) return;
    setLocalError('');
    try {
      const { base64, contentType } = await fetchGravatarBase64(gravatarEmail);
      await updateMutation.mutateAsync({ avatarBase64: base64, avatarContentType: contentType });
      setGravatarEmail('');
    } catch (e) {
      setLocalError(e instanceof Error ? e.message : 'Gravatar error');
    }
  };

  const handleRemove = async () => {
    try {
      await updateMutation.mutateAsync({ removeAvatar: true });
    } catch { setLocalError('Failed to remove avatar'); }
  };

  return (
    <CardContent className="space-y-4">
      {user && (
        <>
          <div className="flex items-center gap-4">
            <div className="w-16 h-16 rounded-full bg-muted flex items-center justify-center overflow-hidden">
              {avatarSrc ? (
                <img src={avatarSrc} alt="Avatar" className="w-full h-full object-cover" />
              ) : (
                <UserIcon className="w-8 h-8 text-muted-foreground" />
              )}
            </div>
            <div>
              <p className="font-medium">{user.username}</p>
              <p className="text-sm text-muted-foreground">{user.opdsPath}</p>
            </div>
          </div>
          {localError && (
            <div className="p-3 text-sm text-red-500 bg-red-50 dark:bg-red-950 rounded-md">{localError}</div>
          )}
          {updateMutation.isSuccess && (
            <div className="p-3 text-sm text-green-600 bg-green-50 dark:bg-green-950 rounded-md">Avatar updated!</div>
          )}
          <div className="space-y-2">
            <Label>Upload Image</Label>
            <Button type="button" variant="outline" size="sm" onClick={() => fileInputRef.current?.click()}>
              <Upload className="w-4 h-4 mr-2" /> Choose File
            </Button>
            <p className="text-xs text-muted-foreground">PNG, JPG, GIF, WebP up to 2MB</p>
            <input ref={fileInputRef} type="file" accept=".png,.jpg,.jpeg,.gif,.webp" className="hidden" onChange={handleFileUpload} />
          </div>
          <div className="space-y-2">
            <Label htmlFor="gravatar">Get from Gravatar</Label>
            <div className="flex gap-2">
              <Input id="gravatar" type="email" placeholder="Enter email" value={gravatarEmail} onChange={(e) => setGravatarEmail(e.target.value)} />
              <Button type="button" variant="secondary" size="sm" onClick={handleGravatar}>Fetch</Button>
            </div>
            <p className="text-xs text-muted-foreground">Email used only on the frontend — never sent to the backend.</p>
          </div>
          {avatarSrc && (
            <Button type="button" variant="outline" size="sm" onClick={handleRemove}>Remove Avatar</Button>
          )}
        </>
      )}
    </CardContent>
  );
}

// ── URL Schema Validation Helpers ──

/** Returns true if the string is empty OR has a valid http/https scheme */
const isValidExternalDomain = (value: string): boolean => {
  if (!value) return true; // empty is allowed
  return /^https?:\/\/./.test(value);
};

/** Returns an error message if the value is non-empty and missing a scheme */
const getExternalDomainError = (value: string): string | null => {
  if (!value) return null;
  if (!/^https?:\/\//.test(value)) {
    return "URL is missing the schema (e.g. https://)";
  }
  return null;
};

// Security Settings Section
function SecuritySection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const authEnabled = localSettings.authenticationEnabled;
  const oidcLocked = localSettings.oidcManagedByConfig === true;
  const externalDomainError = getExternalDomainError(
    localSettings.externalDomain || "",
  );

  return (
    <CardContent className="space-y-4">
      <div className="space-y-2">
        <Label htmlFor="external-domain">External Domain</Label>
        <Input
          id="external-domain"
          value={localSettings.externalDomain || ""}
          onChange={(e) =>
            setLocalSettings((prev) => ({
              ...prev,
              externalDomain: e.target.value,
            }))
          }
          placeholder="https://rensaio.example.com"
        />
        {externalDomainError && (
          <p className="text-xs text-amber-600 dark:text-amber-400 flex items-center gap-1">
            <span>⚠</span>
            <span>{externalDomainError}</span>
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          Base URL used for invite links and OPDS URLs. Fill this in when Rensaiō is
          accessed from outside your local network (e.g. a reverse proxy). When left
          empty, links are generated from the address you're currently using to reach
          the app (e.g. http://192.168.x.x:9833), so users on your LAN get working links.
        </p>
      </div>

      <div className="flex items-center space-x-2">
        <Switch
          id="auth-enabled"
          checked={authEnabled}
          onCheckedChange={(checked) =>
            setLocalSettings((prev) => ({
              ...prev,
              authenticationEnabled: checked,
            }))
          }
        />
        <Label htmlFor="auth-enabled">Enable Authentication</Label>
      </div>

      {authEnabled && (
        <div className="border-muted space-y-3 border-l-2 pl-6">
          <div className="flex items-center space-x-2">
            <Switch
              id="oidc-enabled"
              checked={localSettings.oidcEnabled}
              disabled={oidcLocked}
              onCheckedChange={(checked) =>
                setLocalSettings((prev) => ({ ...prev, oidcEnabled: checked }))
              }
            />
            <Label htmlFor="oidc-enabled">Single Sign-On (OpenID Connect)</Label>
          </div>
          <p className="text-xs text-muted-foreground">
            Let users log in with an identity provider such as Authentik, Keycloak,
            Pocket ID or Authelia. Requires authentication to be enabled. Register a
            client at your provider with the callback URL{" "}
            <code className="font-mono">
              {(localSettings.externalDomain || "https://your-rensaio").replace(/\/$/, "")}
              /api/auth/oidc/callback
            </code>
            .
          </p>
          {oidcLocked && (
            <p className="text-xs text-amber-600 dark:text-amber-400">
              These values come from appsettings.json or environment variables and
              cannot be changed here.
            </p>
          )}
          {localSettings.oidcConfigError && (
            <p className="text-xs text-red-600 dark:text-red-400">
              The Oidc configuration could not be read, so single sign-on is off:{" "}
              {localSettings.oidcConfigError}
            </p>
          )}
          <div className="space-y-2">
            <Label htmlFor="oidc-issuer">Issuer URL</Label>
            <Input
              id="oidc-issuer"
              value={localSettings.oidcIssuer || ""}
              disabled={oidcLocked}
              onChange={(e) =>
                setLocalSettings((prev) => ({ ...prev, oidcIssuer: e.target.value }))
              }
              placeholder="https://id.example.com"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="oidc-client-id">Client ID</Label>
            <Input
              id="oidc-client-id"
              value={localSettings.oidcClientId || ""}
              disabled={oidcLocked}
              onChange={(e) =>
                setLocalSettings((prev) => ({ ...prev, oidcClientId: e.target.value }))
              }
              placeholder="rensaio"
            />
          </div>
          <div className="space-y-2">
            <Label htmlFor="oidc-client-secret">Client Secret</Label>
            <Input
              id="oidc-client-secret"
              type="password"
              value={localSettings.oidcClientSecret || ""}
              disabled={oidcLocked}
              onChange={(e) =>
                setLocalSettings((prev) => ({ ...prev, oidcClientSecret: e.target.value }))
              }
              placeholder={
                localSettings.oidcClientSecretSet && !localSettings.oidcClearClientSecret
                  ? "•••••••• (stored — leave empty to keep)"
                  : "Leave empty for a public client (PKCE only)"
              }
            />
            {localSettings.oidcClientSecretSet && !oidcLocked && (
              <div className="flex items-center space-x-2">
                <Checkbox
                  id="oidc-clear-secret"
                  checked={!!localSettings.oidcClearClientSecret}
                  onCheckedChange={(checked) =>
                    setLocalSettings((prev) => ({
                      ...prev,
                      oidcClearClientSecret: checked === true,
                      oidcClientSecret: checked === true ? "" : prev.oidcClientSecret,
                    }))
                  }
                />
                <Label htmlFor="oidc-clear-secret" className="text-xs font-normal cursor-pointer">
                  Remove the stored secret on save (public client)
                </Label>
              </div>
            )}
          </div>
          <div className="space-y-2">
            <Label htmlFor="oidc-button-label">Login button label</Label>
            <Input
              id="oidc-button-label"
              value={localSettings.oidcButtonLabel || ""}
              disabled={oidcLocked}
              onChange={(e) =>
                setLocalSettings((prev) => ({ ...prev, oidcButtonLabel: e.target.value }))
              }
              placeholder="Single Sign-On"
            />
          </div>
          <p className="text-xs text-muted-foreground">
            Group mapping, auto-registration, claim names and hiding the password form
            are configured in the <code className="font-mono">Oidc</code> section of
            appsettings.json. See the README.
          </p>
        </div>
      )}
    </CardContent>
  );
}

// Contribution Settings Section
function ContributionSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const isEnabled = localSettings.contributionEnabled;
  const { toast } = useToast();
  const verifyMutation = useVerifyContributor();

  const handleVerify = () => {
    const serverUrl = localSettings.contributionServerUrl?.trim();
    const contributorId = localSettings.contributionContributorId?.trim();

    if (!serverUrl || !isValidUrl(serverUrl)) {
      toast({
        title: "Validation Error",
        description: "Enter a valid Contribution Server URL before verifying.",
        variant: "destructive",
      });
      return;
    }

    if (!contributorId) {
      toast({
        title: "Validation Error",
        description: "Enter your Contributor Id before verifying.",
        variant: "destructive",
      });
      return;
    }

    verifyMutation.mutate(
      { serverUrl, contributorId },
      {
        onSuccess: (result) => {
          if (result.contributionVerified) {
            // Persist the verified flag locally so the UI reflects immediately,
            // even before the settings query refetches.
            setLocalSettings((prev) => ({
              ...prev,
              contributionVerified: true,
            }));
            toast({
              title: "Contributor verified",
              description: "Your Contributor Id is valid. Contribution features are now enabled.",
            });
          } else {
            setLocalSettings((prev) => ({
              ...prev,
              contributionVerified: false,
            }));
            toast({
              title: "Verification failed",
              description: result.error ?? "Contributor could not be verified.",
              variant: "destructive",
            });
          }
        },
        onError: (error) => {
          setLocalSettings((prev) => ({
            ...prev,
            contributionVerified: false,
          }));
          toast({
            title: "Verification error",
            description: error instanceof Error ? error.message : "Failed to verify contributor.",
            variant: "destructive",
          });
        },
      },
    );
  };

  return (
    <CardContent className="space-y-4">
      <div className="flex items-center space-x-2">
        <Switch
          id="contribution-enabled"
          checked={localSettings.contributionEnabled}
          onCheckedChange={(checked) => {
            // Toggling the master switch resets the verified state local-only;
            // the backend clears the persisted flag on save when disabled.
            setLocalSettings((prev) => ({
              ...prev,
              contributionEnabled: checked,
              ...(checked ? {} : { contributionVerified: false }),
            }));
          }}
        />
        <div>
          <Label htmlFor="contribution-enabled">Enable Contribution</Label>
          <p className="text-muted-foreground mt-1 text-sm">
            When enabled, mapping actions propagate your series metadata, titles and
            sources into the contribution server.
          </p>
        </div>
      </div>

      {isEnabled && (
        <div className="border-muted space-y-4 border-l-2 pl-6">
          {/* Contributor Id */}
          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <Label htmlFor="contribution-contributor-id">Contributor Id</Label>
              <Input
                id="contribution-contributor-id"
                type="text"
                placeholder="00000000-0000-0000-0000-000000000000"
                value={localSettings.contributionContributorId || ""}
                onChange={(e) =>
                  setLocalSettings((prev) => ({
                    ...prev,
                    contributionContributorId: e.target.value,
                    contributionVerified: false,
                  }))
                }
              />
              <p className="text-muted-foreground mt-1 text-sm">
                Your Contributor UUID. It must be verified against the contribution
                server before any contribution features unlock.
              </p>
            </div>
            <div>
              <Label htmlFor="contribution-server-url">Contribution Server URL</Label>
              <Input
                id="contribution-server-url"
                type="text"
                placeholder="https://contribution.rensaio.net"
                value={localSettings.contributionServerUrl || ""}
                onChange={(e) =>
                  setLocalSettings((prev) => ({
                    ...prev,
                    contributionServerUrl: e.target.value,
                  }))
                }
              />
              <p className="text-muted-foreground mt-1 text-sm">
                URL of the contribution server.
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="outline"
              onClick={handleVerify}
              disabled={verifyMutation.isPending || !localSettings.contributionContributorId?.trim()}
            >
              {verifyMutation.isPending ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Verifying...
                </>
              ) : (
                <>
                  <BadgeCheck className="mr-2 h-4 w-4" />
                  Verify
                </>
              )}
            </Button>

            {localSettings.contributionVerified ? (
              <Badge variant="default" className="gap-1">
                <BadgeCheck className="h-3 w-3" />
                Verified Contributor
              </Badge>
            ) : (
              <Badge variant="secondary" className="gap-1">
                <AlertCircle className="h-3 w-3" />
                Not verified
              </Badge>
            )}
            <span className="text-muted-foreground text-xs">
              {localSettings.contributionVerified
                ? "This Contributor Id is registered and active in the contribution server."
                : "Verify your Contributor Id to enable contribution uploads and the Contribution page."}
            </span>
          </div>
        </div>
      )}
    </CardContent>
  );
}

// Advanced CEF / embedded browser settings — collapsed by default so normal users
// are not exposed to low-level JCEF tuning. These map to the backend DTO's cef*
// fields and flow through the existing Settings API unchanged.
function CefAdvancedSection({
  localSettings,
  setLocalSettings,
}: {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}) {
  const [open, setOpen] = useState(false);

  return (
    <CardContent>
      <div className="flex items-center space-x-2">
        <Switch
          id="advanced-options"
          checked={open}
          onCheckedChange={setOpen}
        />
        <div>
          <Label htmlFor="advanced-options">Advanced</Label>
          <p className="text-muted-foreground mt-1 text-sm">
            Show advanced embedded browser and message-pump tuning options.
          </p>
        </div>
      </div>

      {open && (
        <div className="border-muted space-y-4 border-l-2 pl-6 pt-2">
          <div className="flex items-center space-x-2">
            <Switch
              id="cef-enabled"
              checked={localSettings.cefEnabled}
              onCheckedChange={(checked) =>
                setLocalSettings((prev) => ({ ...prev, cefEnabled: checked }))
              }
            />
            <div>
              <Label htmlFor="cef-enabled">Enable Embedded Browser (CEF/JCEF)</Label>
              <p className="text-muted-foreground mt-1 text-sm">
                Disabling reclaims ~512 MiB and all CEF CPU overhead, but sources requiring a
                browser fall back to direct network / FlareSolverr. Requires restart.
              </p>
            </div>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <div>
              <Label htmlFor="cef-max-renderers">Max Renderer Processes</Label>
              <Input
                id="cef-max-renderers"
                type="number"
                min="1"
                value={localSettings.cefMaxRenderers}
                onChange={(e) =>
                  setLocalSettings((prev) => ({
                    ...prev,
                    cefMaxRenderers: Math.max(1, parseInt(e.target.value) || 1),
                  }))
                }
              />
            </div>
            <div>
              <Label htmlFor="cef-idle-timeout">Pool Idle Timeout (ms)</Label>
              <Input
                id="cef-idle-timeout"
                type="number"
                min="1000"
                value={localSettings.cefIdleTimeoutMs}
                onChange={(e) =>
                  setLocalSettings((prev) => ({
                    ...prev,
                    cefIdleTimeoutMs: Math.max(1000, parseInt(e.target.value) || 300000),
                  }))
                }
              />
            </div>
            <div>
              <Label htmlFor="cef-pump-active">Message Pump Active Interval (ms)</Label>
              <Input
                id="cef-pump-active"
                type="number"
                min="1"
                value={localSettings.cefPumpActiveIntervalMs}
                onChange={(e) =>
                  setLocalSettings((prev) => ({
                    ...prev,
                    cefPumpActiveIntervalMs: Math.max(1, parseInt(e.target.value) || 10),
                  }))
                }
              />
              <p className="text-muted-foreground mt-1 text-sm">
                While browsers are alive (default 10 ms).
              </p>
            </div>
            <div>
              <Label htmlFor="cef-pump-idle">Message Pump Idle Interval (ms)</Label>
              <Input
                id="cef-pump-idle"
                type="number"
                min="50"
                value={localSettings.cefPumpIdleIntervalMs}
                onChange={(e) =>
                  setLocalSettings((prev) => ({
                    ...prev,
                    cefPumpIdleIntervalMs: Math.max(50, parseInt(e.target.value) || 500),
                  }))
                }
              />
              <p className="text-muted-foreground mt-1 text-sm">
                When no browser is open (default 500 ms) — reduces idle CPU burn.
              </p>
            </div>
          </div>

          <div className="flex items-center space-x-2">
            <Switch
              id="cef-pool-enabled"
              checked={localSettings.cefWebViewPoolEnabled}
              onCheckedChange={(checked) =>
                setLocalSettings((prev) => ({ ...prev, cefWebViewPoolEnabled: checked }))
              }
            />
            <Label htmlFor="cef-pool-enabled">WebView Pooling / Reuse</Label>
          </div>
        </div>
      )}
    </CardContent>
  );
}

// Available settings sections
const AVAILABLE_SECTIONS: SettingsSection[] = [
  {
    id: "content-preferences",
    title: "Content Preferences",
    description: "Configure your languages and content filters.",
    component: ContentPreferencesSection,
  },
  {
    id: "storage",
    title: "Storage",
    description: "Configure how archives are stored and organized.",
    component: StorageSection,
  },
  {
    id: "mihon-repositories",
    title: "Mihon Repositories",
    description: "Configure external repositories for additional sources.",
    component: MihonRepositoriesSection,
  },
  {
    id: "download-settings",
    title: "Download Settings",
    description: "Configure download behavior and limits.",
    component: DownloadSettingsSection,
  },
  {
    id: "schedule-tasks",
    title: "Schedule Tasks",
    description: "Configure automatic update schedules and timings.",
    component: ScheduleTasksSection,
  },
  {
    id: "security",
    title: "Security",
    description: "Configure authentication and security settings.",
    component: SecuritySection,
  },
  {
    id: "flaresolverr",
    title: "FlareSolverr Settings",
    description: "Configure FlareSolverr for bypassing Cloudflare protection.",
    component: FlareSolverrSection,
  },
  {
    id: "socks-settings",
    title: "Socks Settings",
    description: "Configure SOCKS proxy settings for sources requests.",
    component: SocksSettingsSection,
  },
  {
    id: "contribution",
    title: "Contribution",
    description: "Configure contribution of your series metadata to the global contribution server.",
    component: ContributionSection,
  },
  {
    id: "advanced-cef",
    title: "Advanced",
    description: "Embedded browser and message-pump tuning. Changes marked (restart) apply on next start.",
    component: CefAdvancedSection,
  },
];

interface SettingsManagerProps {
  /** Which sections to show. If not provided, all sections are shown */
  sections?: string[];
  /** Whether to show the save button */
  showSaveButton?: boolean;
  /** Whether to show an additional save button at the end of the sections */
  showFooterSaveButton?: boolean;
  /** Whether to show the main title and description */
  showHeader?: boolean;
  /** Custom title */
  title?: string;
  /** Custom description */
  description?: string;
  /** Callback when settings are saved */
  onSave?: (settings: Settings) => void;
  /** Callback when settings change */
  onSettingsChange?: (settings: Settings) => void;
  /** Whether to use local state management (for wizards/dialogs) */
  useLocalState?: boolean;
  /** Initial settings (when using local state) */
  initialSettings?: Settings;
  /** Custom class name for the container */
  className?: string;
}

export function SettingsManager({
  sections,
  showSaveButton = true,
  showFooterSaveButton = false,
  showHeader = true,
  title = "Settings",
  description = "Configure your Rensaiō application settings",
  onSave,
  onSettingsChange,
  useLocalState = false,
  initialSettings,
  className = "",
}: SettingsManagerProps) {
  const [localSettings, setLocalSettings] = useState<Settings | null>(
    initialSettings ?? null,
  );
  const { toast } = useToast();
  const router = useRouter();
  const isInitialMount = React.useRef(true);

  const onSettingsChangeRef = React.useRef(onSettingsChange);
  React.useEffect(() => {
    onSettingsChangeRef.current = onSettingsChange;
  });

  // Determine if we should fetch settings from the server
  const shouldFetchSettings =
    !useLocalState || (useLocalState && !initialSettings);

  // Always call the hook, but conditionally use the data
  const { data: settings, isLoading: settingsLoading } = useSettings();
  const updateSettingsMutation = useUpdateSettings();

  // Memoize settings update handler
  const handleSettingsUpdate = React.useCallback(
    (updater: (prev: Settings) => Settings) => {
      setLocalSettings((prev) => (prev ? updater(prev) : prev));
    },
    [],
  );
  // Initialize local settings when data is loaded
  React.useEffect(() => {
    if (settings && shouldFetchSettings) {
      if (!useLocalState) {
        setLocalSettings(settings);
      } else if (useLocalState && !initialSettings) {
        // In local state mode, use fetched settings as fallback if no initial settings provided
        setLocalSettings((prev) => prev ?? settings);
      }
    }
  }, [settings, useLocalState, initialSettings, shouldFetchSettings]);
  // Notify parent of settings changes (skip initial mount to avoid calling with initial state)
  React.useEffect(() => {
    if (isInitialMount.current) {
      isInitialMount.current = false;
      return;
    }

    if (localSettings && onSettingsChangeRef.current) {
      onSettingsChangeRef.current(localSettings);
    }
  }, [localSettings]); // Show loading state while settings are being fetched (only in server state mode)
  if (!useLocalState && (settingsLoading || !localSettings)) {
    return (
      <div className={`space-y-6 ${className}`}>
        {showHeader && (
          <div>
            <h1 className="text-3xl font-bold">{title}</h1>
            <p className="text-muted-foreground">{description}</p>
          </div>
        )}
        <div className="flex items-center justify-center py-12">
          <div className="text-muted-foreground">Loading settings...</div>
        </div>
      </div>
    );
  }

  // In local state mode, show loading only if we're waiting for server data and have no initial settings
  if (useLocalState && !initialSettings && settingsLoading && !localSettings) {
    return (
      <div className={`space-y-6 ${className}`}>
        {showHeader && (
          <div>
            <h1 className="text-3xl font-bold">{title}</h1>
            <p className="text-muted-foreground">{description}</p>
          </div>
        )}
        <div className="flex items-center justify-center py-12">
          <div className="text-muted-foreground">Loading settings...</div>
        </div>
      </div>
    );
  }

  if (!localSettings) {
    return null;
  }

  const handleSave = async () => {
    if (!localSettings) return;

    // Validate externalDomain: if non-empty, it must have a schema
    if (localSettings.externalDomain && !/^https?:\/\//.test(localSettings.externalDomain)) {
      toast({
        title: "Validation Error",
        description: "External Domain is missing the URL schema (e.g. https://)",
        variant: "destructive",
      });
      return;
    }

    try {
      if (onSave) {
        onSave(localSettings);
      } else {
        const result = await updateSettingsMutation.mutateAsync(localSettings);

        // If the backend returned a set-password URL, redirect the user
        // so they can set their password before being locked out.
        // Harden the URL: always navigate on the CURRENT origin so the
        // username/token are never sent to a different host (e.g. a hardcoded
        // http://localhost:9833 when Rensaiō is accessed via a LAN IP).
        if (result?.setPasswordUrl) {
          window.location.href = safeSetPasswordUrl(result.setPasswordUrl);
          return;
        }

        toast({
          title: "Success",
          description: result?.message ?? "Settings saved successfully",
        });
      }
    } catch (error) {
      toast({
        title: "Error",
        description: error instanceof Error ? error.message : "Failed to save settings. Please try again.",
        variant: "destructive",
      });
    }
  };

  // Filter sections based on props
  const sectionsToShow = sections
    ? AVAILABLE_SECTIONS.filter((section) => sections.includes(section.id))
    : AVAILABLE_SECTIONS;

  return (
    <div className={`space-y-6 ${className}`}>
      {showHeader && (
        <div className="flex items-center justify-between">
          <div>
            <p className="text-muted-foreground">{description}</p>
          </div>
          {showSaveButton && (
            <Button
              onClick={handleSave}
              disabled={updateSettingsMutation.isPending}
            >
              {updateSettingsMutation.isPending ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Saving...
                </>
              ) : (
                <>
                  <Save className="mr-2 h-4 w-4" />
                  Save Settings
                </>
              )}
            </Button>
          )}
        </div>
      )}

      <div className="grid gap-6">
        {sectionsToShow.map((section) => {
          const SectionComponent = section.component;
          return (
            <Card key={section.id}>
              <CardHeader>
                <CardTitle>{section.title}</CardTitle>
                <CardDescription>{section.description}</CardDescription>
              </CardHeader>
              <SectionComponent
                localSettings={localSettings}
                setLocalSettings={handleSettingsUpdate}
              />
            </Card>
          );
        })}
      </div>

      {showFooterSaveButton && (
        <div className="flex items-center justify-between">
          <div />
          <Button
            onClick={handleSave}
            disabled={updateSettingsMutation.isPending}
          >
            {updateSettingsMutation.isPending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                Saving...
              </>
            ) : (
              <>
                <Save className="mr-2 h-4 w-4" />
                Save Settings
              </>
            )}
          </Button>
        </div>
      )}
    </div>
  );
}
