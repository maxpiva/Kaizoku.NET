export interface Source {
  id: string;
  name: string;
  displayName: string;
  lang: string;
  iconUrl: string;
  supportsLatest: boolean;
}

export interface Series {
  id: number;
  sourceId: string;
  url: string;
  title: string;
  thumbnailUrl?: string;
  description?: string;
  genre?: string[];
  status?: string;
  author?: string;
  artist?: string;
  inLibrary?: boolean;
  freshData?: boolean;
  realUrl?: string;
}

export interface Chapter {
  id: number;
  url: string;
  name: string;
  uploadDate: number;
  chapterNumber: number;
  scanlator?: string;
  mangaId: number;
  read: boolean;
  bookmarked: boolean;
  lastPageRead: number;
  lastReadAt: number;
  index: number;
  fetchedAt: number;
  realUrl?: string;
  pageCount?: number;
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
  readonly storageFolder: string;
  nsfwVisibility: NsfwVisibility;
  // Setup Wizard properties
  isWizardSetupComplete: boolean;
  wizardSetupStepCompleted: number;
}

export interface LinkedSeries {
  id: string;
  providerId: string;
  provider: string;
  lang: string;
  thumbnailUrl?: string;
  title: string;
  linkedIds: string[];
  useCover: boolean;
  isStorage: boolean;
}

export interface FullSeries {
  id: string;
  providerId: string;
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
  meta: Record<string, string>;
  useCover: boolean;
  isStorage: boolean;
  isUnknown: boolean;
  useTitle: boolean;
  existingProvider: boolean;
  isSelected: boolean;
  isUnselectable?: boolean; // For marking existing series that cannot be selected
  lastUpdatedUTC: string; // ISO 8601 format
  suggestedFilename: string;
  chapters: (number | null)[];
  status: SeriesStatus;
  chapterList: string;
  // Legacy properties for compatibility
  storageFolderPath?: string;
  categories?: string[];
  useCategoriesForPath?: boolean;
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
}
export interface ExistingSource {
  provider: string;
  scanlator: string;
  lang: string;
}

export interface AddSeriesRequest {
  storagePath: string;
  type: string;
  series: FullSeries[];
}

export interface SearchSource {
  sourceId: string;
  sourceName: string;
  language: string;
}

// Setup Wizard related types
export interface Import {
  path: string;
  titles: string[];
  status: ImportStatus;
  info: KaizokuInfo;
  importInfo: ImportInfo;
}

export interface ImportInfo {
  path: string;
  title: string;
  status: ImportStatus;
  continueAfterChapter?: number; // Changed to optional to match backend decimal?
  action: Action; // Added new Action property
  series: SmallSeries[];
}

export interface SmallSeries {
  id: string;
  providerId: string;
  provider: string;
  scanlator: string;
  lang: string;
  thumbnailUrl?: string;
  title: string;
  chapterCount: number;
  url?: string;
  lastChapter?: number;
  preferred: boolean;
  chapterList: string;
  useCover: boolean; // Added new property
  isStorage: boolean; // Added new property
  useTitle: boolean; // Added new property
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

export interface KaizokuInfo {
  path: string;
  title: string;
  artist?: string;
  author?: string;
  description?: string;
  genre?: string[];
  thumbnailUrl?: string;
  providers?: ProviderInfo[];
  // Add other properties as needed based on backend model
}

export interface ProviderInfo {
  provider: string;
  language: string;
}

export interface ProgressState {
  id: string;
  jobType: JobType;
  progressStatus: ProgressStatus;
  percentage: number;
  message: string;
  errorMessage?: string;
  parameter?: any; // Generic parameter object
}

export enum JobType {
  ScanLocalFiles = 0,
  InstallAdditionalExtensions = 1,
  SearchProviders = 2,
  ImportSeries = 3,
  GetChapters = 4,
  GetLatest = 5,
  Download = 6,
  UpdateExtensions =7,
  UpdateAllSeries = 8
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

export interface ImportResponse {
  success: boolean;
  message: string;
  results: Array<{
    path: string;
    status: string;
    reason?: string;
    error?: string;
    providersCount?: number;
    providers?: string[];
  }>;
}

// Import Totals for Schedule Updates step
export interface ImportTotals {
  totalSeries: number;
  totalProviders: number;
  totalDownloads: number;
}

// Provider related types (replacing Extension types)
export interface Provider {
  repo: string;
  name: string;
  pkgName: string;
  versionName: string;
  versionCode: number;
  lang: string;
  apkName: string;
  isNsfw: boolean;
  installed: boolean;
  hasUpdate: boolean;
  iconUrl: string;
  obsolete: boolean;
}

export interface ProviderPreferences {
  apkName: string;
  preferences: ProviderPreference[];
}

export interface ProviderPreference {
  type: EntryType;
  key: string;
  title: string;
  summary: string;
  valueType: ValueType;
  defaultValue: unknown;
  entries: string[];
  entryValues: string[];
  currentValue: unknown;
  source: string;
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
  lastChangeUTC: string;
  lastChangeProvider: SmallProviderInfo;
  isActive: boolean;
  hasUnknown: boolean;
  pausedDownloads: boolean;
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
  meta: Record<string, string>;
  useCover: boolean;
  isStorage: boolean;
  isUnknown: boolean;
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
  url?:string;
  isStorage: boolean;
}

export interface MatchInfo {
  id: string;
  provider: string;
  scanlator: string;
  language: string;
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
  chapterNumber?: number;
  chapterName: string;
  thumbnailUrl?: string;
}

export interface LatestSeriesInfo {
  id: string;
  suwayomiSourceId: string;
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
