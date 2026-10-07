"use client";

import { type AddSeriesState } from "@/components/comp/series/add-series";
import { Badge } from "@/components/ui/badge";
import { Switch } from "@/components/ui/switch";
import { Label } from "@/components/ui/label";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Collapsible, CollapsibleTrigger, CollapsibleContent } from "@/components/ui/collapsible";
import { type FullSeries, type ExistingSource } from "@/lib/api/types";
import React from "react";
import Image from "next/image";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { formatThumbnailUrl } from "@/lib/utils/thumbnail";
import { ChevronDown } from "lucide-react";

// ─── DynamicTags — kept as a named export for external consumers ─────────────
export function DynamicTags({ genres }: { genres: string[] }) {
  const containerRef = React.useRef<HTMLDivElement>(null);
  const [visibleCount, setVisibleCount] = React.useState<number>(6);

  React.useEffect(() => {
    if (!containerRef.current || genres.length === 0) return;

    const calculateVisibleTags = () => {
      const container = containerRef.current;
      if (!container) return;

      const containerWidth = container.offsetWidth;
      let totalWidth = 0;
      let count = 0;

      const tempContainer = document.createElement("div");
      tempContainer.style.position = "absolute";
      tempContainer.style.visibility = "hidden";
      tempContainer.style.display = "flex";
      tempContainer.style.gap = "4px";
      document.body.appendChild(tempContainer);

      for (const genre of genres) {
        const tempBadge = document.createElement("span");
        tempBadge.className =
          "inline-flex items-center rounded-md border px-2.5 py-0.5 text-sm font-semibold transition-colors focus:outline-none focus:ring-2 focus:ring-ring focus:ring-offset-2 border-transparent bg-secondary text-secondary-foreground hover:bg-secondary/80";
        tempBadge.textContent = genre;
        tempContainer.appendChild(tempBadge);
        const tagWidth = tempBadge.offsetWidth + 4;
        if (totalWidth + tagWidth <= containerWidth - 80) {
          totalWidth += tagWidth;
          count++;
        } else {
          break;
        }
      }

      document.body.removeChild(tempContainer);
      setVisibleCount(Math.max(1, count));
    };

    calculateVisibleTags();

    const resizeObserver = new ResizeObserver(calculateVisibleTags);
    resizeObserver.observe(containerRef.current);
    return () => resizeObserver.disconnect();
  }, [genres]);

  return (
    <div ref={containerRef} className="flex items-center gap-1 w-full">
      {genres.slice(0, visibleCount).map((genre: string) => (
        <Badge key={genre} variant="secondary" className="text-sm">
          {genre}
        </Badge>
      ))}
      {genres.length > visibleCount && (
        <Badge variant="secondary" className="text-sm">
          +{genres.length - visibleCount} more
        </Badge>
      )}
    </div>
  );
}

// ─── Type guards (verbatim) ──────────────────────────────────────────────────

function isFullSeriesArray(value: unknown): value is FullSeries[] {
  return Array.isArray(value) && value.every(isValidFullSeries);
}

function isValidFullSeries(obj: unknown): obj is FullSeries {
  if (!obj || typeof obj !== "object") return false;
  const series = obj as Record<string, unknown>;
  return (
    (typeof series.mihonId === "string" || typeof series.providerId === "string") &&
    typeof series.provider === "string" &&
    typeof series.scanlator === "string" &&
    typeof series.lang === "string" &&
    typeof series.title === "string" &&
    typeof series.artist === "string" &&
    typeof series.author === "string" &&
    typeof series.description === "string" &&
    Array.isArray(series.genre) &&
    typeof series.chapterCount === "number" &&
    (typeof series.url === "string" || typeof series.url === "undefined") &&
    typeof series.useCover === "boolean" &&
    typeof series.isStorage === "boolean" &&
    typeof series.useTitle === "boolean"
  );
}

function isExistingSeries(series: FullSeries, existingSources: ExistingSource[]): boolean {
  return existingSources.some(
    (existing) =>
      existing.mihonProviderId === series.mihonProviderId &&
      existing.provider === series.provider &&
      existing.scanlator === series.scanlator &&
      existing.lang === series.lang,
  );
}

const getSeriesId = (series: FullSeries): string =>
  series.mihonId ?? series.providerId ?? series.title;

// ─── Main component ──────────────────────────────────────────────────────────

export function ConfirmSeriesStep({
  formState,
  setFormState,
  setError: _setError,
  setIsLoading: _setIsLoading,
  setCanProgress,
  isAddSourcesMode = false,
  existingSources = [],
  existingPermanentCount = 0,
}: {
  formState: AddSeriesState;
  setFormState: React.Dispatch<React.SetStateAction<AddSeriesState>>;
  setError: React.Dispatch<React.SetStateAction<string | null>>;
  setIsLoading: React.Dispatch<React.SetStateAction<boolean>>;
  setCanProgress: React.Dispatch<React.SetStateAction<boolean>>;
  isAddSourcesMode?: boolean;
  existingSources?: ExistingSource[];
  existingPermanentCount?: number;
}) {
  // ── validFullSeries (verbatim) ─────────────────────────────────────────────
  const validFullSeries: FullSeries[] = React.useMemo(() => {
    if (isFullSeriesArray(formState.fullSeries)) {
      const processedSeries = formState.fullSeries.map((series) => ({
        ...series,
        isUnselectable: isExistingSeries(series, existingSources),
      }));
      return processedSeries.sort((a, b) => {
        if (a.isUnselectable && !b.isUnselectable) return -1;
        if (!a.isUnselectable && b.isUnselectable) return 1;
        return 0;
      });
    }
    return [];
  }, [formState.fullSeries, existingSources]);

  // ── setCanProgress effect (verbatim) ──────────────────────────────────────
  React.useEffect(() => {
    const hasSelectedSeries = validFullSeries.some(
      (series) => series.isSelected && !series.isUnselectable,
    );
    setCanProgress(hasSelectedSeries);
  }, [validFullSeries, setCanProgress]);

  // ── Auto-initialize first selectable (verbatim) ───────────────────────────
  React.useEffect(() => {
    const selectableSeries = validFullSeries.filter((series) => !series.isUnselectable);
    if (selectableSeries.length > 0 && !selectableSeries.some((series) => series.isSelected)) {
      setFormState((prev: AddSeriesState) => {
        const updatedSeries = prev.fullSeries.map((series: FullSeries) => {
          const isFirstSelectable =
            !series.isUnselectable &&
            selectableSeries[0] &&
            getSeriesId(series) === getSeriesId(selectableSeries[0]) &&
            series.provider === selectableSeries[0].provider &&
            series.lang === selectableSeries[0].lang &&
            series.scanlator === selectableSeries[0].scanlator;
          return { ...series, isSelected: isFirstSelectable || false };
        });
        return { ...prev, fullSeries: updatedSeries };
      });
    }
  }, [validFullSeries.length, setFormState]);

  // ── Handlers (verbatim) ───────────────────────────────────────────────────
  const handleToggleSelection = React.useCallback(
    (seriesKey: string) => {
      setFormState((prev: AddSeriesState) => {
        const updatedSeries = prev.fullSeries.map((series: FullSeries) => {
          const currentKey = `${getSeriesId(series)}-${series.provider}-${series.lang}-${series.scanlator}`;
          return currentKey === seriesKey && !series.isUnselectable
            ? { ...series, isSelected: !series.isSelected }
            : series;
        });
        return { ...prev, fullSeries: updatedSeries };
      });
    },
    [setFormState],
  );

  const handleStorageChange = React.useCallback(
    (seriesKey: string, checked: boolean): void => {
      setFormState((prev: AddSeriesState): AddSeriesState => {
        const updatedSeries: FullSeries[] = prev.fullSeries.map(
          (series: FullSeries): FullSeries => {
            const currentKey = `${getSeriesId(series)}-${series.provider}-${series.lang}-${series.scanlator}`;
            return currentKey === seriesKey ? { ...series, isStorage: checked } : series;
          },
        );
        return { ...prev, fullSeries: updatedSeries };
      });
    },
    [setFormState],
  );

  const handleCoverChange = React.useCallback(
    (seriesKey: string, checked: boolean): void => {
      setFormState((prev: AddSeriesState): AddSeriesState => {
        const updatedSeries: FullSeries[] = prev.fullSeries.map(
          (series: FullSeries): FullSeries => {
            const currentKey = `${getSeriesId(series)}-${series.provider}-${series.lang}-${series.scanlator}`;
            return {
              ...series,
              useCover:
                currentKey === seriesKey ? checked : checked ? false : series.useCover,
            };
          },
        );
        return { ...prev, fullSeries: updatedSeries };
      });
    },
    [setFormState],
  );

  const handleTitleChange = React.useCallback(
    (seriesKey: string, checked: boolean): void => {
      setFormState((prev: AddSeriesState): AddSeriesState => {
        const updatedSeries: FullSeries[] = prev.fullSeries.map(
          (series: FullSeries): FullSeries => {
            const currentKey = `${getSeriesId(series)}-${series.provider}-${series.lang}-${series.scanlator}`;
            return {
              ...series,
              useTitle:
                currentKey === seriesKey ? checked : checked ? false : series.useTitle,
            };
          },
        );
        return { ...prev, fullSeries: updatedSeries };
      });
    },
    [setFormState],
  );

  // ── Storage / category state (verbatim) ───────────────────────────────────
  const [selectedCategory, setSelectedCategory] = React.useState<string>("");
  const [editableStoragePath, setEditableStoragePath] = React.useState<string>("");
  const categoryManuallyChanged = React.useRef<boolean>(false);

  const handleStoragePathChange = React.useCallback(
    (newPath: string) => {
      setEditableStoragePath(newPath);
      setFormState((prev: AddSeriesState) => ({ ...prev, storagePath: newPath }));
    },
    [setFormState],
  );

  const handleCategoryChange = React.useCallback((newCategory: string) => {
    categoryManuallyChanged.current = true;
    setSelectedCategory(newCategory);
  }, []);

  // ── titleSeries / useCover series (verbatim logic) ────────────────────────
  const titleSeries = React.useMemo(
    () => validFullSeries.find((series) => series.useTitle),
    [validFullSeries],
  );

  const coverSeries = React.useMemo(
    () =>
      validFullSeries.find((series) => series.useCover) ??
      validFullSeries.find((s) => !s.isUnselectable) ??
      validFullSeries[0],
    [validFullSeries],
  );

  const availableCategories = React.useMemo(
    () => formState.originalAugmentedResponse?.categories ?? [],
    [formState.originalAugmentedResponse],
  );

  const useCategoriesForPath =
    formState.originalAugmentedResponse?.useCategoriesForPath ?? false;
  const baseStoragePath = formState.originalAugmentedResponse?.storageFolderPath;

  const titleSeriesIdRef = React.useRef<string | null>(null);

  // ── Category init effect (verbatim) ───────────────────────────────────────
  React.useEffect(() => {
    const currentTitleSeriesId = titleSeries
      ? `${getSeriesId(titleSeries)}-${titleSeries.provider}`
      : null;
    if (
      currentTitleSeriesId !== titleSeriesIdRef.current &&
      !categoryManuallyChanged.current
    ) {
      titleSeriesIdRef.current = currentTitleSeriesId;
      if (availableCategories.length > 0) {
        const initialCategory =
          titleSeries?.type && availableCategories.includes(titleSeries.type)
            ? titleSeries.type
            : availableCategories[0];
        setSelectedCategory(initialCategory ?? "");
      } else {
        setSelectedCategory("");
      }
    }
  }, [titleSeries, availableCategories]);

  // ── Path-computation effect (verbatim) ────────────────────────────────────
  React.useEffect(() => {
    if (formState.storagePath && !editableStoragePath) {
      setEditableStoragePath(formState.storagePath);
      return;
    }
    if (!titleSeries || !baseStoragePath) return;

    const separator = baseStoragePath.includes("\\") ? "\\" : "/";
    let computedPath: string;
    if (availableCategories.length > 0 && useCategoriesForPath && selectedCategory) {
      computedPath = `${baseStoragePath}${separator}${selectedCategory}${separator}${titleSeries.suggestedFilename}`;
    } else {
      computedPath = `${baseStoragePath}${separator}${titleSeries.suggestedFilename}`;
    }
    if (computedPath !== editableStoragePath) {
      handleStoragePathChange(computedPath);
    }
  }, [
    titleSeries,
    baseStoragePath,
    availableCategories,
    useCategoriesForPath,
    selectedCategory,
    formState.storagePath,
    editableStoragePath,
  ]);

  // ── More-options collapsible state ────────────────────────────────────────
  const [moreOptionsOpen, setMoreOptionsOpen] = React.useState(false);

  // ── Derived display values ────────────────────────────────────────────────
  const displayTitle = titleSeries?.title ?? coverSeries?.title ?? "";
  const displayAuthor = coverSeries?.author ?? "";
  const displayArtist = coverSeries?.artist ?? "";
  const displayGenres = (coverSeries?.genre ?? []).slice(0, 4);
  const displaySynopsis = coverSeries?.description ?? "";
  const coverUrl = coverSeries ? formatThumbnailUrl(coverSeries.thumbnailUrl) : null;

  const selectedCount = validFullSeries.filter(
    (s) => s.isSelected && !s.isUnselectable,
  ).length;

  const permanentCount = existingPermanentCount + validFullSeries.filter(
    (s) => s.isSelected && !s.isUnselectable && s.isStorage,
  ).length;

  return (
    <div className="confirm-scroll">
      <div className="flex flex-col gap-4">
        {/* Title bar */}
        {displayTitle && (
          <div className="confirm-title">{displayTitle}</div>
        )}

        {/* Two-column layout */}
        <div className="cmd-confirm">
        {/* ── Left: meta panel ── */}
        <div>
          {/* Cover */}
          <div className="confirm-cv">
            {coverUrl ? (
              <Image
                src={coverUrl}
                alt={displayTitle}
                fill
                sizes="(max-width: 640px) 240px, 180px"
                className="object-cover"
              />
            ) : (
              <div className="cv-faint">
                {displayTitle
                  .split(" ")
                  .slice(0, 2)
                  .map((w) => w[0])
                  .join("")
                  .toUpperCase()}
              </div>
            )}
          </div>

          {/* Author / artist */}
          {displayAuthor && (
            <p className="text-sm text-muted-foreground italic mt-2">
              by {displayAuthor}
              {displayArtist && displayArtist !== displayAuthor && (
                <> · art {displayArtist}</>
              )}
            </p>
          )}

          {/* Genre chips */}
          {displayGenres.length > 0 && (
            <div className="flex flex-wrap gap-1 mt-2">
              {displayGenres.map((genre: string) => (
                <span key={genre} className="chip">
                  {genre}
                </span>
              ))}
            </div>
          )}

          {/* Synopsis */}
          {displaySynopsis && (
            <p className="confirm-synopsis mt-2">{displaySynopsis}</p>
          )}
        </div>

        {/* ── Right: sources ── */}
        <div className="src-table-wrap">
          <div className="src-table-head">
            <h3>
              Track from <em>these sources</em>
            </h3>
            {selectedCount > 0 && (
              <span className="sel-count">
                <span className="dot" />
                {selectedCount} OF {validFullSeries.filter((s) => !s.isUnselectable).length}
              </span>
            )}
          </div>

          <p className="mb-3 text-xs leading-relaxed text-muted-foreground">
            Permanent sources always download their own copies of chapters and replace
            temporary copies. Temporary sources fill missing chapters. Keep one permanent
            source and use temporary sources as fallbacks to avoid duplicates.
          </p>
          {permanentCount > 1 && (
            <div role="status" className="mb-3 rounded-lg border border-amber-500/40 bg-amber-500/10 p-3 text-sm text-foreground">
              <strong>{permanentCount} permanent sources:</strong> overlapping chapters
              will be downloaded more than once, using extra storage. This includes any
              existing permanent sources. You can continue if you want separate copies.
            </div>
          )}

          {/* Desktop table */}
          <div className="src-table">
            <div className="src-thead">
              <div />
              <div />
              <div>Source</div>
              <div className="ch-head">Chapters</div>
            </div>
            {validFullSeries.map((series) => {
              const seriesKey = `${getSeriesId(series)}-${series.provider}-${series.lang}-${series.scanlator}`;
              const chCount = series.chapterCount;
              const thumbUrl = series.thumbnailUrl ? formatThumbnailUrl(series.thumbnailUrl) : null;
              return (
                <React.Fragment key={seriesKey}>
                  <div
                    className={`src-trow${series.isSelected && !series.isUnselectable ? " active" : ""}${series.isUnselectable ? " opacity-50 cursor-not-allowed" : ""}`}
                    onClick={() => {
                      if (!series.isUnselectable) handleToggleSelection(seriesKey);
                    }}
                  >
                    <input
                      type="checkbox"
                      className="src-check"
                      checked={series.isSelected && !series.isUnselectable}
                      onChange={() => {
                        if (!series.isUnselectable) handleToggleSelection(seriesKey);
                      }}
                      onClick={(e) => e.stopPropagation()}
                      disabled={series.isUnselectable}
                    />
                    <div className="src-cv-mini">
                      {thumbUrl && (
                        <Image
                          src={thumbUrl}
                          alt={`${series.provider} cover`}
                          fill
                          sizes="32px"
                          className="object-cover"
                        />
                      )}
                    </div>
                    <div className="src-name">
                      <span className="name-txt">{series.provider}</span>
                      <span className="flag">
                        <ReactCountryFlag
                          countryCode={getCountryCodeForLanguage(series.lang)}
                          svg
                          style={{ width: "16px", height: "12px" }}
                          title={series.lang.toUpperCase()}
                        />
                      </span>
                      {series.scanlator && series.scanlator !== series.provider && (
                        <span className="text-muted-foreground text-[10px] truncate">
                          · {series.scanlator}
                        </span>
                      )}
                    </div>
                    <div className="ch-cell">
                      <span className="num">{chCount}</span>
                      <span className="lab"> ch</span>
                    </div>
                  </div>

                  {/* Per-source switches — visible only when selected */}
                  {series.isSelected && !series.isUnselectable && (
                    <div className="src-row-extras">
                      <div className="flex items-center gap-1.5">
                        <Switch
                          id={`storage-${seriesKey}`}
                          checked={series.isStorage}
                          onCheckedChange={(checked) =>
                            handleStorageChange(seriesKey, checked)
                          }
                        />
                        <Label
                          htmlFor={`storage-${seriesKey}`}
                          className="text-xs cursor-pointer"
                        >
                          Permanent Source
                        </Label>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <Switch
                          id={`cover-${seriesKey}`}
                          checked={series.useCover}
                          onCheckedChange={(checked) =>
                            handleCoverChange(seriesKey, checked)
                          }
                        />
                        <Label
                          htmlFor={`cover-${seriesKey}`}
                          className="text-xs cursor-pointer"
                        >
                          Use as Cover
                        </Label>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <Switch
                          id={`title-${seriesKey}`}
                          checked={series.useTitle}
                          onCheckedChange={(checked) =>
                            handleTitleChange(seriesKey, checked)
                          }
                        />
                        <Label
                          htmlFor={`title-${seriesKey}`}
                          className="text-xs cursor-pointer"
                        >
                          Use as Title
                        </Label>
                      </div>
                    </div>
                  )}
                </React.Fragment>
              );
            })}
          </div>

          {/* Mobile card stack */}
          <div className="src-cards">
            {validFullSeries.map((series) => {
              const seriesKey = `${getSeriesId(series)}-${series.provider}-${series.lang}-${series.scanlator}`;
              const chCount = series.chapterCount;
              const thumbUrl = series.thumbnailUrl ? formatThumbnailUrl(series.thumbnailUrl) : null;
              return (
                <React.Fragment key={seriesKey}>
                  <div
                    className={`src-card${series.isSelected && !series.isUnselectable ? " active" : ""}${series.isUnselectable ? " opacity-50 cursor-not-allowed" : ""}`}
                    onClick={() => {
                      if (!series.isUnselectable) handleToggleSelection(seriesKey);
                    }}
                  >
                    <input
                      type="checkbox"
                      className="src-check"
                      checked={series.isSelected && !series.isUnselectable}
                      onChange={() => {
                        if (!series.isUnselectable) handleToggleSelection(seriesKey);
                      }}
                      onClick={(e) => e.stopPropagation()}
                      disabled={series.isUnselectable}
                    />
                    <div className="src-cv-mini">
                      {thumbUrl && (
                        <Image
                          src={thumbUrl}
                          alt={`${series.provider} cover`}
                          fill
                          sizes="32px"
                          className="object-cover"
                        />
                      )}
                    </div>
                    <div className="src-info">
                      <div className="name-row">
                        <span className="name-txt">{series.provider}</span>
                        <span className="flag">
                          <ReactCountryFlag
                            countryCode={getCountryCodeForLanguage(series.lang)}
                            svg
                            style={{ width: "16px", height: "12px" }}
                            title={series.lang.toUpperCase()}
                          />
                        </span>
                        {series.scanlator && series.scanlator !== series.provider && (
                          <span className="scanlator">· {series.scanlator}</span>
                        )}
                      </div>
                      <div className="sub-row">
                        <span>{series.lang.toUpperCase()}</span>
                        <span className="sep">·</span>
                        <span>
                          <span className="num">{chCount}</span> chapters
                        </span>
                      </div>
                    </div>
                    {series.isUnselectable && (
                      <span className="text-[10px] text-destructive font-semibold">
                        EXISTS
                      </span>
                    )}
                  </div>

                  {/* Per-source switches — visible only when selected */}
                  {series.isSelected && !series.isUnselectable && (
                    <div className="src-row-extras">
                      <div className="flex items-center gap-1.5">
                        <Switch
                          id={`m-storage-${seriesKey}`}
                          checked={series.isStorage}
                          onCheckedChange={(checked) =>
                            handleStorageChange(seriesKey, checked)
                          }
                        />
                        <Label
                          htmlFor={`m-storage-${seriesKey}`}
                          className="text-xs cursor-pointer"
                        >
                          Permanent
                        </Label>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <Switch
                          id={`m-cover-${seriesKey}`}
                          checked={series.useCover}
                          onCheckedChange={(checked) =>
                            handleCoverChange(seriesKey, checked)
                          }
                        />
                        <Label
                          htmlFor={`m-cover-${seriesKey}`}
                          className="text-xs cursor-pointer"
                        >
                          Cover
                        </Label>
                      </div>
                      <div className="flex items-center gap-1.5">
                        <Switch
                          id={`m-title-${seriesKey}`}
                          checked={series.useTitle}
                          onCheckedChange={(checked) =>
                            handleTitleChange(seriesKey, checked)
                          }
                        />
                        <Label
                          htmlFor={`m-title-${seriesKey}`}
                          className="text-xs cursor-pointer"
                        >
                          Title
                        </Label>
                      </div>
                    </div>
                  )}
                </React.Fragment>
              );
            })}
          </div>

          {/* More options — Storage Path + Category */}
          {!isAddSourcesMode && titleSeries && (
            <Collapsible open={moreOptionsOpen} onOpenChange={setMoreOptionsOpen}>
              <CollapsibleTrigger asChild>
                <button
                  type="button"
                  className="flex items-center gap-1.5 mt-4 text-xs text-muted-foreground hover:text-foreground transition-colors"
                >
                  <ChevronDown
                    className="h-3.5 w-3.5 transition-transform duration-200"
                    style={{
                      transform: moreOptionsOpen ? "rotate(180deg)" : "rotate(0deg)",
                    }}
                  />
                  More options
                </button>
              </CollapsibleTrigger>
              <CollapsibleContent>
                <div className="flex flex-col sm:flex-row gap-2 sm:gap-4 sm:items-end mt-3">
                  <div className="flex-1 min-w-0">
                    <Label htmlFor="storage-path" className="text-sm font-medium">
                      Storage Path
                    </Label>
                    <Input
                      id="storage-path"
                      value={editableStoragePath}
                      onChange={(e) => handleStoragePathChange(e.target.value)}
                      placeholder="Enter storage path..."
                      className="mt-1 bg-card text-xs sm:text-sm"
                    />
                  </div>
                  {availableCategories.length > 0 && (
                    <div className="w-full sm:w-48">
                      <Label htmlFor="category-select" className="text-sm font-medium">
                        Category
                      </Label>
                      <Select value={selectedCategory} onValueChange={handleCategoryChange}>
                        <SelectTrigger id="category-select" className="mt-1 bg-card">
                          <SelectValue placeholder="Select category" />
                        </SelectTrigger>
                        <SelectContent>
                          {availableCategories.filter((category: string) => category).map((category: string) => (
                            <SelectItem key={category} value={category}>
                              {category}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </div>
                  )}
                </div>
              </CollapsibleContent>
            </Collapsible>
          )}
        </div>
      </div>
      </div>
    </div>
  );
}
