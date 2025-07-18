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

export interface MultiSelectOption {
  value: string;
  label: string;
}

interface MultiSelectProps {
  options: MultiSelectOption[];
  selectedValues: string[];
  onSelectionChange: (selectedValues: string[]) => void;
  placeholder?: string;
  className?: string;
}

export function MultiSelect({
  options,
  selectedValues,
  onSelectionChange,
  placeholder = "Select options...",
  className,
}: MultiSelectProps) {
  const handleToggleAll = () => {
    if (selectedValues.length === options.length) {
      onSelectionChange([]);
    } else {
      onSelectionChange(options.map((option) => option.value));
    }
  };

  const handleToggleOption = (value: string) => {
    if (selectedValues.includes(value)) {
      onSelectionChange(selectedValues.filter((v) => v !== value));
    } else {
      onSelectionChange([...selectedValues, value]);
    }
  };

  const getDisplayText = () => {
    const count = selectedValues.length;
    const total = options.length;

    if (count === 0) return placeholder;
    if (count === total) return "All selected";
    return `${count} selected`;
  };

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="outline"
          role="combobox"
          className={`w-full justify-between bg-card text-left font-normal ${className ?? ''}`}
        >
          <span className="truncate">{getDisplayText()}</span>
          <ChevronDown className="ml-2 h-4 w-4 shrink-0 opacity-50" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent className="w-80" align="start">
        {options.length > 1 && (
          <>
            <DropdownMenuItem
              className="flex items-center space-x-2 cursor-pointer"
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
              className="flex items-center space-x-2 cursor-pointer"
              onSelect={(e) => {
                e.preventDefault();
                handleToggleOption(option.value);
              }}
            >
              <Checkbox
                checked={selectedValues.includes(option.value)}
                className="pointer-events-none"
              />
              <span className="text-sm flex-1">{option.label}</span>
            </DropdownMenuItem>
          ))}
        </div>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
