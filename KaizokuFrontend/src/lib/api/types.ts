export interface Chapter {
  name?: string;
  number?: number;
  providerUploadDate?: string; // ISO 8601 format
  url?: string;
  providerIndex: number;
  downloadDate?: string; // ISO 8601 format
  shouldDownload: boolean;
  isDeleted: boolean;
  pageCount?: number;
  filename?: string;
  chapterNumber?: number;
  index: number;
}

export interface Settings {
  preferredLanguages: string[];
  mihonRepositories: string[];
  numberOfSimultaneousDownloads: number;
  numberOfSimultaneousDownloadsPerProvider: number;
  numberOfSimultaneousSearches: number;
  chapterDownloadFailRetryTime: string; // TimeSpan as string
  chapterDownloadFailRetries: number;
  perTitleUpdateSchedule: string; // TimeSpan as string
  perSourceUpdateSchedule: string; // TimeSpan as string
  extensionsCheckForUpdateSchedule: string; // TimeSpan as string
  categorizedFolders: boolean;
  categories: string[];
  flareSolverrEnabled: boolean;
  flareSolverrUrl: string;
  flareSolverrTimeout: string; // TimeSpan as string
  flareSolverrSessionTtl: string; // TimeSpan as string
  flareSolverrAsResponseFallback: boolean;
  storageFolder: string;
  socksProxyEnabled: boolean;
  socksProxyVersion: number;
  socksProxyHost: string;
  socksProxyPort: number;
  socksProxyUsername: string;
  socksProxyPassword: string;
  nsfwVisibility: NsfwVisibility;
  // Setup Wizard properties
  isWizardSetupComplete: boolean;
  wizardSetupStepCompleted: number;
}

export interface LinkedSeries {
  mihonId?: string;
  mihonProviderId?: string;
  bridgeItemInfo?: string;
  providerId: string;
  provider: string;
  lang: string;
  thumbnailUrl?: string;
  title: string;
  linkedIds: string[];
  useCover: boolean;
  isStorage: boolean;
  isLocal: boolean;
}

export interface FullSeries {
  mihonId?: string;
  mihonProviderId?: string;
  bridgeItemInfo?: string;
  providerId?: string;
  provider: string;
  scanlator: string;
  lang: string;
  thumbnailUrl?: string;
  title: string;
  artist: string;
  author: string;
  description: string;
  genre: string[];
  type?: string;
  chapterCount: number;
  fromChapter?: number; // Maps to ContinueAfterChapter from backend
  url?: string;
  useCover: boolean;
  isStorage: boolean;
  isLocal: boolean;
  isUnknown: boolean;
  useTitle: boolean;
  existingProvider: boolean;
  isSelected: boolean;
  isUnselectable?: boolean; // For marking existing series that cannot be selected
  lastUpdatedUTC: string; // ISO 8601 format
  suggestedFilename: string;
  chapters: Chapter[];
  status: SeriesStatus;
  chapterList: string;
}

export enum SeriesStatus {
  UNKNOWN = 0,
  ONGOING = 1,
  COMPLETED = 2,
  LICENSED = 3,
  PUBLISHING_FINISHED = 4,
  CANCELLED = 5,
  ON_HIATUS = 6,
  DISABLED = 7,
}

export enum NsfwVisibility {
  AlwaysHide = "AlwaysHide",
  HideByDefault = "HideByDefault",
  Show = "Show",
}

export enum InLibraryStatus {
  NotInLibrary = 0,
  InLibrary = 1,
  InLibraryButDisabled = 2,
}

export interface AugmentedResponse {
  storageFolderPath: string;
  useCategoriesForPath: boolean;
  existingSeries: boolean;
  existingSeriesId?: string; // Guid from backend represented as string
  categories: string[];
  series: FullSeries[];
  preferredLanguages: string[];
  disableJobs?: boolean;
  startChapter?: number;
}
export interface ExistingSource {
  provider: string;
  scanlator: string;
  lang: string;
  mihonProviderId: string;
}

export interface AddSeriesRequest {
  storagePath: string;
  type: string;
  series: FullSeries[];
}

export interface SearchSource {
  mihonProviderId: string;
  provider: string;
  scanlator: string;
  language: string;
  isStorage: boolean;
  thumbnailUrl?: string;
  status?: SeriesStatus;
  url?: string;
}

export interface ImportInfo {
  path: string;
  title: string;
  status: ImportStatus;
  continueAfterChapter?: number; // decimal in backend
  action: Action;
  series?: SmallSeries[];
  artist: string;
  author: string;
  description: string;
  genre: string[];
  type: string;
  chapterCount: number;
  lastUpdatedUtc?: string; // ISO 8601 format
  providers: ImportProviderSnapshot[];
  seriesStatus: SeriesStatus;
  isDisabled: boolean;
  kaizokuVersion: number;
}

export interface SmallSeries {
  mihonId?: string;
  mihonProviderId?: string;
  bridgeItemInfo?: string;
  provider: string;
  scanlator: string;
  lang: string;
  thumbnailUrl?: string;
  title: string;
  chapterCount: number;
  url?: string;
  chapterList: string;
  useCover: boolean;
  isStorage: boolean;
  isLocal: boolean;
  useTitle: boolean;
  lastChapter?: number;
  preferred: boolean;
}

export enum Action {
  Add = 0,
  Skip = 1,
}

export enum ImportStatus {
  Import = 0,
  Skip = 1,
  DoNotChange = 2,
  Completed = 3,
}

export interface ProgressState {
  id: string;
  jobType: JobType;
  download?: DownloadCardInfo;
  progressStatus: ProgressStatus;
  percentage: number;
  message: string;
  errorMessage?: string;
}

export enum JobType {
  ScanLocalFiles = 0,
  InstallAdditionalExtensions = 1,
  SearchProviders = 2,
  ImportSeries = 3,
  GetChapters = 4,
  GetLatest = 5,
  Download = 6,
  UpdateExtensions = 7,
  UpdateAllSeries = 8,
  DailyUpdate = 9,
}

export enum ProgressStatus {
  Started = 0,
  InProgress = 1,
  Completed = 2,
  Failed = 3,
}

// Setup Wizard API Response Types
export interface SetupOperationResponse {
  success: boolean;
  message: string;
}

// Import Totals for Schedule Updates step
export interface ImportTotals {
  totalSeries: number;
  totalProviders: number;
  totalDownloads: number;
}

// Provider related types
export interface Provider {
  package: string;
  name: string;
  thumbnailUrl: string;
  isStorage: boolean;
  isEnabled: boolean;
  isBroken: boolean;
  isDead: boolean;
  isInstaled: boolean;
  activeEntry: number;
  autoUpdate: boolean;
  onlineRepositories: ExtensionRepository[];
}

export interface ExtensionRepository {
  name: string;
  id: string;
  entries: ExtensionEntry[];
}

export interface ExtensionEntry {
  id: string;
  onlineRepositoryName: string;
  onlineRepositoryId: string;
  isLocal: boolean;
  name: string;
  downloadUTC: string; // ISO 8601 format
  package: string;
  version: string;
  nsfw: boolean;
  sources: ExtensionSource[];
}

export interface ExtensionSource {
  name: string;
  lang: string;
}

export interface ProviderPreferences {
  pkgName: string;
  preferences: ProviderPreference[];
  provider?: string;
  scanlator?: string;
  language?: string;
  isStorage?: boolean;
  title?: string;
  thumbnailUrl?: string;
  status?: SeriesStatus;
  url?: string;
}

export interface ProviderPreference {
  type: EntryType;
  index: number;
  title: string;
  summary?: string;
  valueType: ValueType;
  defaultValue?: unknown;
  entries?: string[];
  entryValues?: string[];
  currentValue?: unknown;
  languages: string[];
}

export enum EntryType {
  ComboBox = 0,
  ComboCheckBox = 1,
  TextBox = 2,
  Switch = 3,
}

export enum ValueType {
  String = 0,
  StringCollection = 1,
  Boolean = 2,
}

export interface BaseSeriesInfo {
  id: string;
  title: string;
  thumbnailUrl: string;
  artist: string;
  author: string;
  description: string;
  genre: string[];
  status: SeriesStatus;
  storagePath: string;
  type?: string;
  chapterCount: number;
  lastChapter?: number;
  lastChangeUTC?: string | null;
  lastChangeProvider: SmallProviderInfo;
  isActive: boolean;
  hasUnknown: boolean;
  pausedDownloads: boolean;
  startFromChapter?: number;
}

export interface SeriesInfo extends BaseSeriesInfo {
  providers: SmallProviderInfo[];
}

export interface SeriesExtendedInfo extends BaseSeriesInfo {
  providers: ProviderExtendedInfo[];
  chapterList: string;
  path?: string;
}

export interface ProviderExtendedInfo {
  id: string;
  provider: string;
  scanlator: string;
  lang: string;
  thumbnailUrl?: string;
  title: string;
  artist: string;
  author: string;
  description: string;
  genre: string[];
  type?: string;
  chapterCount: number;
  fromChapter?: number;
  url?: string;
  useCover: boolean;
  isStorage: boolean;
  isUnknown: boolean;
  isLocal: boolean;
  useTitle: boolean;
  isDisabled: boolean;
  isUninstalled: boolean;
  isDeleted: boolean;
  lastUpdatedUTC: string;
  status: SeriesStatus;
  lastChapter?: number;
  lastChangeUTC: string;
  chapterList: string;
  matchId: string;
}

export interface DownloadInfoList {
  totalCount: number;
  downloads: DownloadInfo[];
}

export interface DownloadInfo {
  id: string;
  title: string;
  chapter?: number; // Backend uses decimal?, mapped to number in frontend
  chapterTitle?: string;
  provider: string;
  scanlator?: string;
  language: string;
  downloadDateUTC?: string; // ISO 8601 format, nullable in backend
  status: QueueStatus;
  scheduledDateUTC: string; // ISO 8601 format
  retries: number;
  thumbnailUrl?: string;
  url?: string;
}

export interface DownloadsMetrics {
  downloads: number;  // Active downloads count
  queued: number;     // Queued downloads count  
  failed: number;     // Failed downloads count
}

export enum QueueStatus {
  WAITING = 0,
  RUNNING = 1,
  COMPLETED = 2,
  FAILED = 3,
}

export interface SmallProviderInfo {
  provider: string;
  scanlator: string;
  language: string;
  isStorage: boolean;
  title?: string;
  thumbnailUrl?: string;
  status?: SeriesStatus;
  url?: string;
}

export interface MatchInfo {
  id: string;
  provider: string;
  scanlator: string;
  language: string;
  isStorage?: boolean;
  title?: string;
  thumbnailUrl?: string;
  status?: SeriesStatus;
  url?: string;
}

export interface ImportProviderSnapshot {
  provider: string;
  scanlator: string;
  language: string;
  isStorage: boolean;
  title?: string;
  thumbnailUrl?: string;
  status?: SeriesStatus;
  url?: string;
  chapterCount: number;
  chapterList: StartStop[];
  isDisabled: boolean;
  archives: ProviderArchiveSnapshot[];
}

export interface StartStop {
  start: number;
  end: number;
}

export interface ProviderArchiveSnapshot {
  path: string;
  updatedUtc?: string;
  chapterMetrics: ImportChapterMetrics;
}

export interface ImportChapterMetrics {
  totalChapters: number;
  totalPages: number;
  totalMissingPages: number;
}

export interface ProviderMatchChapter {
  filename: string;
  chapterName: string;
  chapterNumber?: number;
  matchInfoId?: string;
}

export interface ProviderMatch {
  id: string;
  matchInfos: MatchInfo[];
  chapters: ProviderMatchChapter[];
}

// Download-related types
export interface DownloadCardInfo {
  pageCount: number;
  provider: string;
  language: string;
  scanlator?: string;
  title: string;
  url?: string;
  chapterNumber?: number;
  chapterName: string;
  thumbnailUrl?: string;
}

export interface LatestSeriesInfo {
  mihonId: string;
  mihonProviderId?: string;
  provider: string;
  language: string;
  url?: string;
  title: string;
  thumbnailUrl?: string;
  artist?: string;
  author?: string;
  description?: string;
  genre: string[];
  fetchDate: string; // ISO 8601 format
  chapterCount?: number;
  latestChapter?: number;
  latestChapterTitle: string;
  status: SeriesStatus;
  inLibrary: InLibraryStatus;
  seriesId?: string; // Guid from backend represented as string
}

export enum ArchiveResult {
  Fine = 'Fine',
  NotAnArchive = 'NotAnArchive',
  NoImages = 'NoImages',
  NotFound = 'NotFound',
}

export enum ErrorDownloadAction {
  Retry = 0,
  Delete = 1,
}

export interface ArchiveIntegrityResult {
  result: ArchiveResult;
  filename: string;
}

export interface SeriesIntegrityResult {
  success: boolean;
  badFiles: ArchiveIntegrityResult[];
}
