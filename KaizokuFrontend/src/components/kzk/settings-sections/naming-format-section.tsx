"use client";

import React, { useRef, useMemo, useState } from 'react';
import { CardContent } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Loader2, FolderSync } from "lucide-react";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import { type Settings } from "@/lib/api/types";
import { useToast } from "@/hooks/use-toast";

// Sample data for preview generation
const SAMPLE_DATA = {
  Series: "One Piece",
  Chapter: "1089",
  Volume: "105",
  Provider: "MangaDex",
  Scanlator: "TCB Scans",
  Language: "en",
  Title: "The Decisive Battle",
  Year: "2024",
  Month: "01",
  Day: "15",
  Type: "Manga",
};

// File name template variables
const FILE_NAME_VARIABLES = [
  { name: "{Series}", description: "Series title" },
  { name: "{Chapter}", description: "Chapter number" },
  { name: "{Chapter:000}", description: "Chapter with 3-digit padding (001, 002)" },
  { name: "{Chapter:0000}", description: "Chapter with 4-digit padding (0001, 0002)" },
  { name: "{Volume}", description: "Volume number" },
  { name: "{Volume:00}", description: "Volume with 2-digit padding (01, 02)" },
  { name: "{Provider}", description: "Source provider" },
  { name: "{Scanlator}", description: "Scanlator group" },
  { name: "{Language}", description: "Language code" },
  { name: "{Title}", description: "Chapter title" },
  { name: "{Year}", description: "Year" },
  { name: "{Month}", description: "Month" },
  { name: "{Day}", description: "Day" },
];

// Folder template variables
const FOLDER_VARIABLES = [
  { name: "{Series}", description: "Series title" },
  { name: "{Type}", description: "Content type (Manga, Manhwa, etc.)" },
  { name: "{Provider}", description: "Source provider" },
  { name: "{Language}", description: "Language code" },
  { name: "{Year}", description: "Year" },
];

// Output format options
const OUTPUT_FORMATS = [
  { value: "0", label: "CBZ (Comic Book ZIP)" },
  { value: "1", label: "PDF (Portable Document Format)" },
];

interface NamingFormatSectionProps {
  localSettings: Settings;
  setLocalSettings: (updater: (prev: Settings) => Settings) => void;
}

// Variable Chip Component
function VariableChip({
  variable,
  onClick
}: {
  variable: { name: string; description: string };
  onClick: () => void;
}) {
  return (
    <Badge
      variant="outline"
      className="cursor-pointer hover:bg-primary hover:text-primary-foreground transition-colors"
      onClick={onClick}
      title={variable.description}
    >
      {variable.name}
    </Badge>
  );
}

// Template Input with Variable Chips
function TemplateInput({
  id,
  label,
  description,
  value,
  onChange,
  variables,
  preview,
}: {
  id: string;
  label: string;
  description: string;
  value: string;
  onChange: (value: string) => void;
  variables: Array<{ name: string; description: string }>;
  preview: string;
}) {
  const inputRef = useRef<HTMLInputElement>(null);

  const handleChipClick = (variableName: string) => {
    const input = inputRef.current;
    if (input) {
      const start = input.selectionStart ?? value.length;
      const end = input.selectionEnd ?? value.length;
      const newValue = value.slice(0, start) + variableName + value.slice(end);
      onChange(newValue);

      // Restore focus and set cursor position after the inserted variable
      setTimeout(() => {
        input.focus();
        const newPosition = start + variableName.length;
        input.setSelectionRange(newPosition, newPosition);
      }, 0);
    } else {
      // Fallback: append to end
      onChange(value + variableName);
    }
  };

  return (
    <div className="space-y-3">
      <div>
        <Label htmlFor={id}>{label}</Label>
        <Input
          ref={inputRef}
          id={id}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={`Enter ${label.toLowerCase()}...`}
          className="mt-1.5"
        />
        <p className="text-sm text-muted-foreground mt-1">
          {description}
        </p>
      </div>

      <div className="space-y-2">
        <Label className="text-xs text-muted-foreground">Click to insert variable:</Label>
        <div className="flex flex-wrap gap-1.5">
          {variables.map((variable) => (
            <VariableChip
              key={variable.name}
              variable={variable}
              onClick={() => handleChipClick(variable.name)}
            />
          ))}
        </div>
      </div>

      <div className="p-3 bg-muted/50 rounded-md border">
        <p className="text-sm">
          <span className="font-medium text-muted-foreground">Preview: </span>
          <span className="font-mono text-primary">{preview}</span>
        </p>
      </div>
    </div>
  );
}

// Generate preview from template
function generatePreview(
  template: string,
  sampleData: typeof SAMPLE_DATA,
  outputFormat: number,
  includeChapterTitle: boolean,
  isFolder: boolean = false
): string {
  if (!template) {
    return isFolder ? "Manga/One Piece/" : "[MangaDex][en] One Piece 1089.cbz";
  }

  let result = template;

  // Replace standard variables
  Object.entries(sampleData).forEach(([key, value]) => {
    const regex = new RegExp(`\\{${key}\\}`, 'gi');
    result = result.replace(regex, value);
  });

  // Handle {Chapter:XXX} padding patterns
  result = result.replace(/\{Chapter:(\d+)\}/gi, (_, padding) => {
    const chapterNum = parseInt(sampleData.Chapter, 10);
    return chapterNum.toString().padStart(padding.length, '0');
  });

  // Handle {Volume:XX} padding patterns
  result = result.replace(/\{Volume:(\d+)\}/gi, (_, padding) => {
    const volumeNum = parseInt(sampleData.Volume, 10);
    return volumeNum.toString().padStart(padding.length, '0');
  });

  // Handle title inclusion
  if (!includeChapterTitle) {
    // Remove title from the preview if it was included
    result = result.replace(new RegExp(`\\s*-?\\s*${sampleData.Title}`, 'gi'), '');
  }

  // Add file extension for file names (not folders)
  if (!isFolder) {
    const extension = outputFormat === 1 ? ".pdf" : ".cbz";
    if (!result.endsWith(extension)) {
      result = result + extension;
    }
  } else {
    // Ensure folder paths end with /
    if (!result.endsWith('/')) {
      result = result + '/';
    }
  }

  return result;
}

export function NamingFormatSection({
  localSettings,
  setLocalSettings
}: NamingFormatSectionProps) {
  const { toast } = useToast();
  const [isRenaming, setIsRenaming] = useState(false);
  const [showRenameDialog, setShowRenameDialog] = useState(false);

  // Derive default values if not set
  const fileNameTemplate = localSettings.fileNameTemplate ?? "[{Provider}][{Language}] {Series} {Chapter}";
  const folderTemplate = localSettings.folderTemplate ?? "{Type}/{Series}";
  const outputFormat = localSettings.outputFormat ?? 0;
  const includeChapterTitle = localSettings.includeChapterTitle ?? false;

  // Generate live previews
  const fileNamePreview = useMemo(() =>
    generatePreview(
      fileNameTemplate,
      SAMPLE_DATA,
      outputFormat,
      includeChapterTitle,
      false
    ),
    [fileNameTemplate, outputFormat, includeChapterTitle]
  );

  const folderPreview = useMemo(() =>
    generatePreview(
      folderTemplate,
      SAMPLE_DATA,
      outputFormat,
      includeChapterTitle,
      true
    ),
    [folderTemplate, outputFormat, includeChapterTitle]
  );

  const handleRenameFiles = async () => {
    setIsRenaming(true);
    try {
      const response = await fetch('/api/settings/rename-files', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
      });

      if (!response.ok) {
        throw new Error('Failed to rename files');
      }

      const result = await response.json();
      toast({
        title: "Rename Started",
        description: `Renaming ${result.totalFiles ?? 'all'} files to match current naming scheme. This may take a while.`,
      });
    } catch (error) {
      toast({
        title: "Error",
        description: "Failed to start file renaming. Please try again.",
        variant: "destructive",
      });
    } finally {
      setIsRenaming(false);
    }
  };

  return (
    <CardContent className="space-y-6">
      {/* File Name Template */}
      <TemplateInput
        id="file-name-template"
        label="File Name Template"
        description="Define how downloaded chapter files are named. Use padding like {Chapter:000} for zero-padded numbers."
        value={fileNameTemplate}
        onChange={(value) => setLocalSettings(prev => ({
          ...prev,
          fileNameTemplate: value
        }))}
        variables={FILE_NAME_VARIABLES}
        preview={fileNamePreview}
      />

      {/* Folder Structure Template */}
      <TemplateInput
        id="folder-template"
        label="Folder Structure Template"
        description="Define the folder hierarchy for organizing series"
        value={folderTemplate}
        onChange={(value) => setLocalSettings(prev => ({
          ...prev,
          folderTemplate: value
        }))}
        variables={FOLDER_VARIABLES}
        preview={folderPreview}
      />

      {/* Output Format */}
      <div className="space-y-2">
        <Label htmlFor="output-format">Output Format</Label>
        <Select
          value={outputFormat.toString()}
          onValueChange={(value) => setLocalSettings(prev => ({
            ...prev,
            outputFormat: parseInt(value, 10)
          }))}
        >
          <SelectTrigger id="output-format" className="w-full md:w-64">
            <SelectValue placeholder="Select format" />
          </SelectTrigger>
          <SelectContent>
            {OUTPUT_FORMATS.map((format) => (
              <SelectItem key={format.value} value={format.value}>
                {format.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <p className="text-sm text-muted-foreground">
          File format for downloaded chapters
        </p>
      </div>

      {/* Include Chapter Title Toggle */}
      <div className="flex items-center justify-between rounded-lg border p-4">
        <div className="space-y-0.5">
          <Label htmlFor="include-chapter-title" className="text-base">
            Include Chapter Title
          </Label>
          <p className="text-sm text-muted-foreground">
            Include chapter title in filename when available from the source
          </p>
        </div>
        <Switch
          id="include-chapter-title"
          checked={includeChapterTitle}
          onCheckedChange={(checked) => setLocalSettings(prev => ({
            ...prev,
            includeChapterTitle: checked
          }))}
        />
      </div>

      {/* Rename Existing Files */}
      <div className="flex items-center justify-between rounded-lg border p-4 border-dashed">
        <div className="space-y-0.5">
          <Label className="text-base">
            Rename Existing Files
          </Label>
          <p className="text-sm text-muted-foreground">
            Rename all existing downloaded files to match the current naming scheme. Save settings first.
          </p>
        </div>
        <Dialog open={showRenameDialog} onOpenChange={setShowRenameDialog}>
          <DialogTrigger asChild>
            <Button variant="outline" disabled={isRenaming}>
              {isRenaming ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Renaming...
                </>
              ) : (
                <>
                  <FolderSync className="mr-2 h-4 w-4" />
                  Rename Files
                </>
              )}
            </Button>
          </DialogTrigger>
          <DialogContent>
            <DialogHeader>
              <DialogTitle>Rename All Files?</DialogTitle>
              <DialogDescription>
                This will rename all existing downloaded files to match your current naming scheme.
                Make sure you have saved your settings first. This operation cannot be undone.
              </DialogDescription>
            </DialogHeader>
            <DialogFooter>
              <Button variant="outline" onClick={() => setShowRenameDialog(false)}>
                Cancel
              </Button>
              <Button onClick={() => { handleRenameFiles(); setShowRenameDialog(false); }}>
                Rename All Files
              </Button>
            </DialogFooter>
          </DialogContent>
        </Dialog>
      </div>
    </CardContent>
  );
}
