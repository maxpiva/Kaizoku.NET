"use client";

import * as React from "react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Checkbox } from "@/components/ui/checkbox";
import { ChevronDown, Globe } from "lucide-react";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";
import type { SearchSource } from "@/lib/api/types";

interface MultiSelectSourcesProps {
  sources: SearchSource[];
  selectedSources: string[];
  onSelectionChange: (selectedSources: string[]) => void;
  placeholder?: string;
  isDesktop?: boolean;
}

export function MultiSelectSources({
  sources,
  selectedSources,
  onSelectionChange,
  placeholder = "Select sources...",
  isDesktop = true,
}: MultiSelectSourcesProps) {
  const handleToggleAll = () => {
    const newSelection = selectedSources.length === sources.length ? [] : sources.map((source) => source.sourceId);
    onSelectionChange(newSelection);
  };

  const handleToggleSource = (sourceId: string) => {
    const newSelection = selectedSources.includes(sourceId)
      ? selectedSources.filter((id) => id !== sourceId)
      : [...selectedSources, sourceId];
    onSelectionChange(newSelection);
  };

  const getDisplayText = () => {
    const count = selectedSources.length;
    const total = sources.length;

    if (count === 0) return placeholder;
    if (count === total) return "All sources";
    return `${count} source${count > 1 ? "s" : ""}`;
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          className="w-full justify-between bg-card text-left font-normal"
        >
          <span className="truncate">{getDisplayText()}</span>
          <ChevronDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-80" align="start">
        <DropdownMenuItem
          className="flex items-center space-x-2 cursor-pointer"
          onSelect={(e) => {
            e.preventDefault();
            handleToggleAll();
          }}
        >          <Checkbox
            checked={selectedSources.length === sources.length}
            className="pointer-events-none"
          />
          <span className="text-sm font-medium">Select All</span>
        </DropdownMenuItem>
        <DropdownMenuSeparator />
        <div className="max-h-60 overflow-y-auto">
          {sources.map((source) => (
            <DropdownMenuItem
              key={source.sourceId}
              className="flex items-center space-x-2 cursor-pointer"
              onSelect={(e) => {
                e.preventDefault();
                handleToggleSource(source.sourceId);
              }}
            >              <Checkbox
                checked={selectedSources.includes(source.sourceId)}
                className="pointer-events-none"
              />
              <div className="flex items-center gap-2 text-sm flex-1">
                {source.language === "all" ? (
                  <Globe size={isDesktop ? 16 : 13} />
                ) : (
                  <ReactCountryFlag
                    countryCode={getCountryCodeForLanguage(source.language)}
                    svg
                    style={{
                      width: isDesktop ? "16px" : "13px",
                      height: isDesktop ? "12px" : "10px",
                    }}
                    title={`${source.language.toUpperCase()}`}
                  />
                )}
                <span>{source.sourceName}</span>
              </div>
            </DropdownMenuItem>
          ))}
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
