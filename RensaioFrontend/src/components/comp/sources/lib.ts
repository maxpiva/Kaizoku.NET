// NOTE: `Provider.isInstaled` is misspelled in the API type. Do NOT "fix" it here.
// The typo is the source of truth; renaming it would cascade across the codebase
// and is out of scope for this refactor.

import { formatThumbnailUrl } from "@/lib/utils/thumbnail";
import { type Provider, type ExtensionEntry } from "@/lib/api/types";

// Re-export so callers importing from "./lib" (e.g. source-thumb.tsx) continue to work.
export { formatThumbnailUrl };

export const getExtensionEntries = (extension: Provider): ExtensionEntry[] =>
  extension.onlineRepositories.flatMap((repo) => repo.entries);

export const getInstalledRepoEntries = (extension: Provider): ExtensionEntry[] | undefined => {
  if (!extension.isInstaled) return undefined;
  const repo = extension.onlineRepositories[0];
  return repo?.entries;
};

export const getPrimaryEntry = (extension: Provider): ExtensionEntry | undefined => {
  const allEntries = getExtensionEntries(extension);
  if (allEntries.length === 0) return undefined;

  if (extension.isInstaled) {
    // The first repository in onlineRepositories contains all locally installed
    // versions for this extension. Use activeEntry to pick the correct one.
    const installedRepo = extension.onlineRepositories[0];
    if (installedRepo?.entries.length) {
      const index = Math.min(
        Math.max(extension.activeEntry ?? 0, 0),
        installedRepo.entries.length - 1
      );
      return installedRepo.entries[index] ?? installedRepo.entries[0];
    }
  }

  return allEntries[0];
};

export const getExtensionLanguages = (extension: Provider): string[] => {
  const langs = getExtensionEntries(extension)
    .flatMap((entry) => entry.sources.map((source) => source.lang))
    .filter(Boolean);
  return Array.from(new Set(langs));
};

export const getPrimaryLanguage = (extension: Provider): string =>
  getExtensionLanguages(extension)[0] ?? "all";

export const getExtensionVersion = (extension: Provider): string =>
  getPrimaryEntry(extension)?.version ?? "";

export const isExtensionNsfw = (extension: Provider): boolean =>
  getExtensionEntries(extension).some((entry) => entry.nsfw);

/** Returns true if the active entry for an installed extension has isLocal = true */
export const isActiveEntryLocal = (extension: Provider): boolean => {
  const entry = getPrimaryEntry(extension);
  return entry?.isLocal ?? false;
};

/** Returns the total number of version entries available for an installed extension */
export const getEntryCount = (extension: Provider): number => {
  if (!extension.isInstaled) return 0;
  return getInstalledRepoEntries(extension)?.length ?? 0;
};
