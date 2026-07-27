"use client";

import React, { useState, useEffect, useCallback, memo, useRef } from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Checkbox } from "@/components/ui/checkbox";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Globe, Plus } from "lucide-react";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import { type ProviderMatch, type ProviderMatchChapter, type MatchInfo, NEW_PROVIDER_SENTINEL } from "@/lib/api/types";

// Memoized row component to prevent unnecessary re-renders
const ChapterRow = memo(({
  chapter,
  index,
  isSelected,
  onMouseDown,
  onMouseEnter,
  onChapterChange,
  renderProviderWithFlag
}: {
  chapter: ProviderMatchChapter;
  index: number;
  isSelected: boolean;
  onMouseDown: (index: number) => void;
  onMouseEnter: (index: number) => void;
  onChapterChange: (index: number, field: keyof ProviderMatchChapter, value: string | number | null) => void;
  renderProviderWithFlag: (matchInfoId: string | null | undefined) => React.ReactNode;
}) => {
  const fieldLabel = "mb-1 block text-[11px] font-medium uppercase tracking-wide opacity-70 sm:hidden";

  return (
    <div
      className={`m-1 flex flex-col gap-2 rounded border p-2 cursor-pointer transition-colors sm:flex-row sm:items-center sm:gap-4 sm:border-transparent ${
        isSelected
          ? 'bg-primary text-primary-foreground border-primary'
          : 'bg-card hover:bg-muted sm:bg-transparent'
      }`}
      onMouseDown={() => onMouseDown(index)}
      onMouseEnter={() => onMouseEnter(index)}
    >
      <div className="w-full min-w-0 sm:w-[35%]">
        <span className={fieldLabel}>Filename</span>
        <Input
          className="h-8 text-sm sm:h-7"
          value={chapter.filename}
          onChange={(e) => onChapterChange(index, 'filename', e.target.value)}
          onMouseDown={(e) => e.stopPropagation()}
        />
      </div>
      <div className="w-full min-w-0 sm:w-[35%]">
        <span className={fieldLabel}>Name</span>
        <Input
          value={chapter.chapterName}
          onChange={(e) => onChapterChange(index, 'chapterName', e.target.value)}
          className="h-8 text-sm sm:h-7"
          onMouseDown={(e) => e.stopPropagation()}
        />
      </div>
      <div className="flex w-full items-end gap-2 sm:contents">
        <div className="min-w-0 flex-1 sm:w-[20%] sm:flex-none">
          <span className={fieldLabel}>Source</span>
          <div className="h-8 px-3 py-1 text-sm text-foreground bg-muted rounded border border-input flex items-center pointer-events-none select-none sm:h-7">
            {renderProviderWithFlag(chapter.matchInfoId)}
          </div>
        </div>
        <div className="w-20 shrink-0 sm:w-[10%]">
          <span className={fieldLabel}>Number</span>
          <Input
            type="number"
            step="0.1"
            value={chapter.chapterNumber || ""}
            onChange={(e) => onChapterChange(index, 'chapterNumber', e.target.value ? parseFloat(e.target.value) : null)}
            className="h-8 text-sm text-right tabular-nums font-mono sm:h-7"
            onMouseDown={(e) => e.stopPropagation()}
          />
        </div>
      </div>
    </div>
  );
});

ChapterRow.displayName = 'ChapterRow';

interface ProviderMatchDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  providerMatch: ProviderMatch | null;
  onSave: (updatedMatch: ProviderMatch) => void;
  isLoading?: boolean;
  isLoadingData?: boolean;
  deletedProviderStates?: Record<string, boolean>;
}

export function ProviderMatchDialog({
  open,
  onOpenChange,
  providerMatch,
  onSave,
  isLoading = false,
  isLoadingData = false,
  deletedProviderStates = {}
}: ProviderMatchDialogProps) {
  const [chapters, setChapters] = useState<ProviderMatchChapter[]>([]);
  const [selectedChapterIndexes, setSelectedChapterIndexes] = useState<Set<number>>(new Set());
  const [selectedMatchInfoId, setSelectedMatchInfoId] = useState<string>("");
  const [rangeStart, setRangeStart] = useState<string>("");
  const [rangeStep, setRangeStep] = useState<string>("1");
  const [isPainting, setIsPainting] = useState<boolean>(false);
  const [paintMode, setPaintMode] = useState<'select' | 'deselect'>('select');
  const [scrollContainer, setScrollContainer] = useState<HTMLDivElement | null>(null);
  const [autoScrollInterval, setAutoScrollInterval] = useState<NodeJS.Timeout | null>(null);

  // New provider creation state
  const [showNewProviderForm, setShowNewProviderForm] = useState(false);
  const [newProviderName, setNewProviderName] = useState("");
  const [newProviderScanlator, setNewProviderScanlator] = useState("");
  const [newProviderLanguage, setNewProviderLanguage] = useState("en");

  // Use a ref to track painting state for auto-scroll interval
  const isPaintingRef = useRef<boolean>(false);
  // Track the last painted index to fill gaps when dragging quickly
  const lastPaintedIndexRef = useRef<number | null>(null);

  // Initialize state when providerMatch changes
  useEffect(() => {
    // Handle both lowercase and uppercase property names from backend
    const chapters = providerMatch?.chapters || (providerMatch as any)?.Chapters;
    if (providerMatch && chapters) {
      setChapters([...chapters]);
      setSelectedChapterIndexes(new Set());
      setSelectedMatchInfoId("");
      setRangeStart("");
      setRangeStep("1");
      setShowNewProviderForm(false);
      setNewProviderName("");
      setNewProviderScanlator("");
      setNewProviderLanguage("en");
    }
  }, [providerMatch]);

  // Update range start when selection changes
  useEffect(() => {
    if (selectedChapterIndexes.size > 0) {
      const firstSelectedIndex = Math.min(...selectedChapterIndexes);
      const firstChapter = chapters[firstSelectedIndex];
      if (firstChapter?.chapterNumber) {
        setRangeStart(firstChapter.chapterNumber.toString());
      }
    }
  }, [selectedChapterIndexes, chapters]);

  const handleChapterChange = useCallback((index: number, field: keyof ProviderMatchChapter, value: string | number | null) => {
    setChapters(prev => {
      const updatedChapters = [...prev];
      updatedChapters[index] = {
        ...updatedChapters[index],
        [field]: value
      } as ProviderMatchChapter;
      return updatedChapters;
    });
  }, []);

  const handleMouseDown = useCallback((index: number) => {
    setSelectedChapterIndexes(prev => {
      const isSelected = prev.has(index);
      const newSelection = new Set(prev);
      
      if (isSelected) {
        newSelection.delete(index);
        setPaintMode('deselect');
      } else {
        newSelection.add(index);
        setPaintMode('select');
      }
        setIsPainting(true);
      isPaintingRef.current = true;
      lastPaintedIndexRef.current = index;
      return newSelection;
    });
  }, []);

  const handleMouseEnter = useCallback((index: number) => {
    if (!isPainting) return;
    
    setSelectedChapterIndexes(prev => {
      const newSelection = new Set(prev);
      
      // Fill in any gaps between the last painted index and current index
      const lastIndex = lastPaintedIndexRef.current;
      if (lastIndex !== null && lastIndex !== index) {
        const start = Math.min(lastIndex, index);
        const end = Math.max(lastIndex, index);
        
        // Fill in all indexes between start and end (inclusive)
        for (let i = start; i <= end; i++) {
          if (paintMode === 'select') {
            newSelection.add(i);
          } else {
            newSelection.delete(i);
          }
        }
      } else {
        // No gap to fill, just handle the current index
        if (paintMode === 'select') {
          newSelection.add(index);
        } else {
          newSelection.delete(index);
        }
      }
      
      // Update the last painted index
      lastPaintedIndexRef.current = index;
      
      return newSelection;
    });
  }, [isPainting, paintMode]);

  const handleMouseMove = useCallback((e: React.MouseEvent) => {
    if (!isPainting || !scrollContainer) return;

    const rect = scrollContainer.getBoundingClientRect();
    const mouseY = e.clientY;
    const scrollTop = scrollContainer.scrollTop;
    const scrollHeight = scrollContainer.scrollHeight;
    const clientHeight = scrollContainer.clientHeight;
      const SCROLL_ZONE = 40; // pixels from edge to trigger scroll
    const SCROLL_SPEED = 3; // pixels per frame
    
    // Only auto-scroll if we're actively painting
    if (!isPainting) {
      if (autoScrollInterval) {
        clearInterval(autoScrollInterval);
        setAutoScrollInterval(null);
      }
      return;
    }
    
    // Check if mouse is near top edge
    if (mouseY < rect.top + SCROLL_ZONE && scrollTop > 0) {
      if (!autoScrollInterval) {
        const interval = setInterval(() => {
          // Double-check that we're still painting before scrolling
          if (!isPaintingRef.current || scrollContainer.scrollTop <= 0) {
            clearInterval(interval);
            setAutoScrollInterval(null);
            return;
          }
          scrollContainer.scrollTop -= SCROLL_SPEED;
        }, 16); // ~60fps
        setAutoScrollInterval(interval);
      }
    }
    // Check if mouse is near bottom edge
    else if (mouseY > rect.bottom - SCROLL_ZONE && scrollTop < scrollHeight - clientHeight) {
      if (!autoScrollInterval) {
        const interval = setInterval(() => {
          // Double-check that we're still painting before scrolling
          if (!isPaintingRef.current || scrollContainer.scrollTop >= scrollContainer.scrollHeight - scrollContainer.clientHeight) {
            clearInterval(interval);
            setAutoScrollInterval(null);
            return;
          }
          scrollContainer.scrollTop += SCROLL_SPEED;
        }, 16); // ~60fps
        setAutoScrollInterval(interval);
      }
    }
    // Stop auto-scrolling if mouse is not in scroll zones OR not painting
    else {
      if (autoScrollInterval) {
        clearInterval(autoScrollInterval);
        setAutoScrollInterval(null);
      }
    }
  }, [isPainting, scrollContainer, autoScrollInterval]);

  const handleMouseUp = useCallback(() => {
    setIsPainting(false);
    isPaintingRef.current = false;
    lastPaintedIndexRef.current = null;
    if (autoScrollInterval) {
      clearInterval(autoScrollInterval);
      setAutoScrollInterval(null);
    }
  }, [autoScrollInterval]);

  // Add global mouse up listener
  useEffect(() => {
    const handleGlobalMouseUp = () => {
      setIsPainting(false);
      isPaintingRef.current = false;
      lastPaintedIndexRef.current = null;
      if (autoScrollInterval) {
        clearInterval(autoScrollInterval);
        setAutoScrollInterval(null);
      }
    };
    document.addEventListener('mouseup', handleGlobalMouseUp);
    return () => document.removeEventListener('mouseup', handleGlobalMouseUp);
  }, [autoScrollInterval]);

  // Cleanup auto-scroll interval on unmount
  useEffect(() => {
    return () => {
      if (autoScrollInterval) {
        clearInterval(autoScrollInterval);
      }
    };
  }, [autoScrollInterval]);

  // Add keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.ctrlKey && e.key === 'a') {
        e.preventDefault();
        setSelectedChapterIndexes(new Set(chapters.map((_, index) => index)));
      }
    };

    if (open) {
      document.addEventListener('keydown', handleKeyDown);
      return () => document.removeEventListener('keydown', handleKeyDown);
    }
  }, [open, chapters]);

  const handleSelectAll = (checked: boolean) => {
    if (checked) {
      setSelectedChapterIndexes(new Set(chapters.map((_, index) => index)));
    } else {
      setSelectedChapterIndexes(new Set());
    }
  };

  const handleSelectAllButton = () => {
    setSelectedChapterIndexes(new Set(chapters.map((_, index) => index)));
  };

  const handleSelectNone = () => {
    setSelectedChapterIndexes(new Set());
  };

  const handleMatchSelected = () => {
    if (!selectedMatchInfoId) return;

    const updatedChapters = [...chapters];
    selectedChapterIndexes.forEach(index => {
      updatedChapters[index] = {
        ...updatedChapters[index],
        matchInfoId: selectedMatchInfoId
      } as ProviderMatchChapter;
    });
    setChapters(updatedChapters);
  };

  const handleFillRange = () => {
    const start = parseFloat(rangeStart);
    const step = parseFloat(rangeStep);

    if (isNaN(start) || isNaN(step)) return;

    const updatedChapters = [...chapters];
    const sortedIndexes = Array.from(selectedChapterIndexes).sort((a, b) => a - b);
    
    sortedIndexes.forEach((index, i) => {
      updatedChapters[index] = {
        ...updatedChapters[index],
        chapterNumber: start + (i * step)
      } as ProviderMatchChapter;
    });
    
    setChapters(updatedChapters);
  };

  // Handle selection of "Create New Provider" from the dropdown
  const handleProviderSelect = useCallback((value: string) => {
    if (value === "__new__") {
      setShowNewProviderForm(true);
      setSelectedMatchInfoId("");
    } else {
      setSelectedMatchInfoId(value);
      setShowNewProviderForm(false);
    }
  }, []);

  // Confirm creation of new provider
  const handleNewProviderConfirm = useCallback(() => {
    if (!newProviderName.trim()) return;
    // Use the sentinel as the matchInfoId - the backend will create the provider
    setSelectedMatchInfoId(NEW_PROVIDER_SENTINEL);
    setShowNewProviderForm(false);
  }, [newProviderName]);

  // Cancel new provider creation
  const handleNewProviderCancel = useCallback(() => {
    setShowNewProviderForm(false);
    setNewProviderName("");
    setNewProviderScanlator("");
    setNewProviderLanguage("en");
  }, []);

  // Get the correct property names (handle both cases)
  const backendChapters = providerMatch?.chapters || (providerMatch as any)?.Chapters;
  const backendMatchInfos = providerMatch?.matchInfos || (providerMatch as any)?.MatchInfos;

  // Filter out deleted providers from the available match infos
  const availableMatchInfos = React.useMemo(() => {
    if (!backendMatchInfos) return [];
    
    return backendMatchInfos.filter((matchInfo: any) => {
      // Check if this provider is marked as deleted using the matchInfo.id
      const isDeleted = deletedProviderStates[matchInfo.id] === true;
      return !isDeleted;
    });
  }, [backendMatchInfos, deletedProviderStates]);

  const getMatchInfo = useCallback((matchInfoId: string | null | undefined): any => {
    // Handle the sentinel (new provider) case
    if (matchInfoId === NEW_PROVIDER_SENTINEL) {
      return {
        id: NEW_PROVIDER_SENTINEL,
        provider: newProviderName || "New Provider",
        scanlator: newProviderScanlator,
        language: newProviderLanguage
      };
    }
    // Use filtered available match infos instead of backend match infos
    if (!matchInfoId || !availableMatchInfos) return null;
    return availableMatchInfos.find((m: any) => m.id === matchInfoId);
  }, [availableMatchInfos, newProviderName, newProviderScanlator, newProviderLanguage]);

  const getProviderName = useCallback((matchInfoId: string | null | undefined): string => {
    const matchInfo = getMatchInfo(matchInfoId);
    return matchInfo?.provider || "Unknown";
  }, [getMatchInfo]);

  const renderProviderWithFlag = useCallback((matchInfoId: string | null | undefined) => {
    const matchInfo = getMatchInfo(matchInfoId);
    if (!matchInfo) {
      return (
        <div className="flex items-center gap-2 text-sm">
          <span>Unknown</span>
        </div>
      );
    }

    const language = matchInfo.language || "all";
    
    return (
      <div className="flex items-center gap-2 text-sm">
        {language === "all" ? (
          <Globe size={16} />
        ) : (
          <ReactCountryFlag
            countryCode={getCountryCodeForLanguage(language)}
            svg
            style={{
              width: "16px",
              height: "12px",
            }}
            title={`${language.toUpperCase()}`}
          />
        )}
        <span>{matchInfo.provider}{(matchInfo.provider!=matchInfo.scanlator && matchInfo.scanlator) ? ` • ${matchInfo.scanlator}` : ''}</span>
      </div>
    );
  }, [getMatchInfo]);

  // A chapter is valid if it has a matchInfoId or is assigned to the sentinel (new provider)
  const canSave = chapters.every(chapter => {
    if (!chapter.matchInfoId) return false;
    if (chapter.matchInfoId === NEW_PROVIDER_SENTINEL) {
      // When using sentinel, ensure we have provider metadata
      return newProviderName.trim().length > 0;
    }
    return true;
  });

  const handleSave = () => {
    if (!providerMatch || !canSave) return;

    // Build the matchInfos list, including the sentinel entry if needed
    const hasNewProvider = chapters.some(ch => ch.matchInfoId === NEW_PROVIDER_SENTINEL);
    let matchInfos = backendMatchInfos ? [...backendMatchInfos] : [];

    if (hasNewProvider) {
      // Add the sentinel MatchInfo so the backend knows what provider to create
      matchInfos.push({
        id: NEW_PROVIDER_SENTINEL,
        provider: newProviderName,
        scanlator: newProviderScanlator,
        language: newProviderLanguage
      });
    }

    const updatedMatch: ProviderMatch = {
      ...providerMatch,
      matchInfos,
      chapters
    };
    onSave(updatedMatch);
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="w-[95vw] max-w-[95vw] sm:max-w-[85vw] h-[92dvh] max-h-[92dvh] sm:h-[88vh] sm:max-h-[88vh] flex flex-col gap-3 sm:gap-4 bg-background overflow-x-hidden overflow-y-hidden">
        <DialogHeader className="shrink-0 pr-10">
          <DialogTitle>Match Source to Chapters</DialogTitle>
        </DialogHeader>

        {/* Show loading state when data is being fetched */}
        {isLoadingData ? (
          <div className="flex-1 flex items-center justify-center min-h-64">
            <div className="text-center">
              <div className="text-lg font-medium">Loading match data...</div>
              <div className="text-sm text-muted-foreground mt-2">Please wait while we fetch the sources information.</div>
            </div>
          </div>
        ) : !providerMatch || !backendChapters ? (
          <div className="flex-1 flex items-center justify-center min-h-64">
            <div className="text-center">
              <div className="text-lg font-medium text-red-500">Failed to load match data</div>
              <div className="text-sm text-muted-foreground mt-2">Please try again.</div>
            </div>
          </div>
        ) : (
          <div className="flex-1 flex flex-col gap-3 sm:gap-4 min-h-0">
            {/* Chapter list container — owns its own scrolling, never overlaps the controls */}
            <div className="flex-1 flex flex-col min-h-0 overflow-hidden border rounded-md bg-card">
              <div className="shrink-0 p-3 border-b bg-muted">
                <div className="flex items-center gap-4 text-sm font-medium">
                  <Checkbox
                    checked={selectedChapterIndexes.size === chapters.length && chapters.length > 0}
                    onCheckedChange={handleSelectAll}
                  />
                  <span className="sm:hidden">Select all</span>
                  <div className="hidden sm:block sm:w-[35%]">Filename</div>
                  <div className="hidden sm:block sm:w-[35%]">Name</div>
                  <div className="hidden sm:block sm:w-[20%]">Source</div>
                  <div className="hidden sm:block sm:w-[10%]">Number</div>
                </div>
              </div>
              <div
                className="flex-1 min-h-0 overflow-y-auto overscroll-contain"
                onMouseUp={handleMouseUp}
                onMouseMove={handleMouseMove}
                ref={setScrollContainer}
              >
                <div className="p-2 select-none">
                  {chapters.map((chapter, index) => (
                    <ChapterRow
                      key={index}
                      chapter={chapter}
                      index={index}
                      isSelected={selectedChapterIndexes.has(index)}
                      onMouseDown={handleMouseDown}
                      onMouseEnter={handleMouseEnter}
                      onChapterChange={handleChapterChange}
                      renderProviderWithFlag={renderProviderWithFlag}
                    />
                  ))}
                </div>
              </div>
            </div>

            {/* Bottom control area — separate solid container, sits below the list */}
            <div className="shrink-0 flex flex-col gap-3 p-3 border rounded-md bg-card">
              <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between sm:gap-4">
                <div className="flex items-center gap-2 sm:gap-4">
                  <Label className="text-sm font-medium shrink-0">Providers:</Label>
                  <Select
                    value={selectedMatchInfoId === NEW_PROVIDER_SENTINEL ? "__new__" : selectedMatchInfoId}
                    onValueChange={handleProviderSelect}
                  >
                    <SelectTrigger className="flex-1 min-w-0 sm:flex-none sm:w-48">
                      <SelectValue placeholder="Select source">
                        {selectedMatchInfoId && renderProviderWithFlag(selectedMatchInfoId)}
                      </SelectValue>
                    </SelectTrigger>
                    <SelectContent>
                      {availableMatchInfos?.filter((matchInfo: any) => matchInfo.id).map((matchInfo: any) => (
                        <SelectItem key={matchInfo.id} value={matchInfo.id}>
                          {renderProviderWithFlag(matchInfo.id)}
                        </SelectItem>
                      )) || []}
                      <SelectItem value="__new__">
                        <div className="flex items-center gap-2 text-sm">
                          <Plus size={16} />
                          <span>Create New Provider</span>
                        </div>
                      </SelectItem>
                    </SelectContent>
                  </Select>
                  <Button
                    onClick={handleMatchSelected}
                    disabled={!selectedMatchInfoId || selectedChapterIndexes.size === 0 || selectedMatchInfoId === "__new__"}
                    size="sm"
                    className="shrink-0"
                  >
                    Match
                  </Button>
                </div>

                <div className="flex items-center gap-2 sm:gap-4">
                  <Label className="text-sm font-medium shrink-0">Range Fill:</Label>
                  <Input
                    type="number"
                    step="0.1"
                    placeholder="Start"
                    value={rangeStart}
                    onChange={(e) => setRangeStart(e.target.value)}
                    className="w-full min-w-0 h-8 sm:w-20"
                  />
                  <Input
                    type="number"
                    step="0.1"
                    placeholder="Step"
                    value={rangeStep}
                    onChange={(e) => setRangeStep(e.target.value)}
                    className="w-full min-w-0 h-8 sm:w-20"
                  />
                  <Button
                    onClick={handleFillRange}
                    disabled={!rangeStart || !rangeStep || selectedChapterIndexes.size === 0}
                    size="sm"
                    className="shrink-0"
                  >
                    Fill
                  </Button>
                </div>
              </div>

              {/* New provider creation form - shown when "Create New Provider" is selected */}
              {showNewProviderForm && (
                <div className="flex flex-col gap-3 p-3 bg-background border rounded-md sm:flex-row sm:items-end sm:gap-4">
                  <div className="flex flex-col gap-1">
                    <Label className="text-xs text-muted-foreground">Name</Label>
                    <Input
                      placeholder="Provider name"
                      value={newProviderName}
                      onChange={(e) => setNewProviderName(e.target.value)}
                      className="h-8 w-full sm:w-44"
                    />
                  </div>
                  <div className="flex flex-col gap-1">
                    <Label className="text-xs text-muted-foreground">Scanlator</Label>
                    <Input
                      placeholder="Scanlator (optional)"
                      value={newProviderScanlator}
                      onChange={(e) => setNewProviderScanlator(e.target.value)}
                      className="h-8 w-full sm:w-36"
                    />
                  </div>
                  <div className="flex flex-col gap-1">
                    <Label className="text-xs text-muted-foreground">Language</Label>
                    <Input
                      placeholder="en"
                      value={newProviderLanguage}
                      onChange={(e) => setNewProviderLanguage(e.target.value)}
                      className="h-8 w-full sm:w-20"
                    />
                  </div>
                  <div className="flex items-end gap-2 sm:mt-auto">
                    <Button
                      onClick={handleNewProviderConfirm}
                      disabled={!newProviderName.trim()}
                      size="sm"
                      variant="default"
                    >
                      Create
                    </Button>
                    <Button
                      onClick={handleNewProviderCancel}
                      size="sm"
                      variant="outline"
                    >
                      Cancel
                    </Button>
                  </div>
                </div>
              )}
            </div>
          </div>
        )}
        <DialogFooter className="shrink-0 gap-2 sm:gap-0">
          <div className="flex items-center justify-center text-sm text-muted-foreground sm:mr-auto">
            ({selectedChapterIndexes.size} of {chapters.length} selected)
          </div>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={handleSave}
            disabled={!canSave || isLoading}
          >
            {isLoading ? "Saving..." : "OK"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
