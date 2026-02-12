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
import { ChevronDown } from "lucide-react";
import ReactCountryFlag from "react-country-flag";
import { getCountryCodeForLanguage } from "@/lib/utils/language-country-mapping";

export interface LanguageOption {
  value: string;
  label: string;
}

interface MultiSelectLanguagesProps {
  options: LanguageOption[];
  selectedValues: string[];
  onSelectionChange: (selectedValues: string[]) => void;
  placeholder?: string;
  className?: string;
}

export function MultiSelectLanguages({
  options,
  selectedValues,
  onSelectionChange,
  placeholder = "Languages...",
  className,
}: MultiSelectLanguagesProps) {
  const handleToggleAll = () => {
    if (selectedValues.length === options.length) {
      onSelectionChange([]);
    } else {
      onSelectionChange(options.map((o) => o.value));
    }
  };

  const handleToggleOption = (value: string) => {
    if (selectedValues.includes(value)) {
      onSelectionChange(selectedValues.filter((v) => v !== value));
    } else {
      onSelectionChange([...selectedValues, value]);
    }
  };

  const renderTriggerContent = () => {
    const count = selectedValues.length;
    const total = options.length;

    if (count === 0) return <span className="truncate">{placeholder}</span>;
    if (count === total)
      return <span className="truncate">All languages</span>;

    // Show inline flags + codes for up to 3 selections
    if (count <= 3) {
      const selected = options.filter((o) => selectedValues.includes(o.value));
      return (
        <span className="flex items-center gap-1.5 truncate">
          {selected.map((lang) => (
            <span key={lang.value} className="flex items-center gap-0.5">
              <ReactCountryFlag
                countryCode={getCountryCodeForLanguage(lang.value)}
                svg
                style={{ width: "14px", height: "10px" }}
              />
              <span className="text-xs">{lang.value}</span>
            </span>
          ))}
        </span>
      );
    }

    return <span className="truncate">{count} languages</span>;
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          className={`w-full justify-between bg-card text-left font-normal ${className ?? ""}`}
        >
          {renderTriggerContent()}
          <ChevronDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-64" align="start">
        {options.length > 1 && (
          <>
            <DropdownMenuItem
              className="flex cursor-pointer items-center space-x-2"
              onSelect={(e) => {
                e.preventDefault();
                handleToggleAll();
              }}
            >
              <Checkbox
                checked={selectedValues.length === options.length}
                className="pointer-events-none"
              />
              <span className="text-sm font-medium">Select All</span>
            </DropdownMenuItem>
            <DropdownMenuSeparator />
          </>
        )}
        <div className="max-h-60 overflow-y-auto">
          {options.map((option) => (
            <DropdownMenuItem
              key={option.value}
              className="flex cursor-pointer items-center space-x-2"
              onSelect={(e) => {
                e.preventDefault();
                handleToggleOption(option.value);
              }}
            >
              <Checkbox
                checked={selectedValues.includes(option.value)}
                className="pointer-events-none"
              />
              <div className="flex flex-1 items-center gap-2 text-sm">
                <ReactCountryFlag
                  countryCode={getCountryCodeForLanguage(option.value)}
                  svg
                  style={{ width: "16px", height: "12px" }}
                  title={option.value.toUpperCase()}
                />
                <span>{option.label}</span>
              </div>
            </DropdownMenuItem>
          ))}
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
