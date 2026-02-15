"use client";

import React, { useState } from "react";
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
import { Badge } from "@/components/ui/badge";
import { Plus, X, Save, Loader2, GripVertical } from "lucide-react";
import {
  useSettings,
  useAvailableLanguages,
  useUpdateSettings,
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

// Available settings sections
const AVAILABLE_SECTIONS: SettingsSection[] = [
  {
    id: "content-preferences",
    title: "Content Preferences",
    description: "Configure your languages and content filters.",
    component: ContentPreferencesSection,
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
    id: "storage",
    title: "Storage",
    description: "Configure how archives are stored and organized.",
    component: StorageSection,
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
];

interface SettingsManagerProps {
  /** Which sections to show. If not provided, all sections are shown */
  sections?: string[];
  /** Whether to show the save button */
  showSaveButton?: boolean;
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
  showHeader = true,
  title = "Settings",
  description = "Configure your Kaizoku.NET application settings",
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

    try {
      if (onSave) {
        onSave(localSettings);
      } else {
        await updateSettingsMutation.mutateAsync(localSettings);
        toast({
          title: "Success",
          description: "Settings saved successfully",
        });
      }
    } catch (error) {
      toast({
        title: "Error",
        description: "Failed to save settings",
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
    </div>
  );
}
