// Sentinel GUID used to signal creation of a new user-based provider in the match flow
export const NEW_PROVIDER_SENTINEL = "00000000-0000-0000-0000-000000000000";

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
  // Security settings
  authenticationEnabled: boolean;
  externalDomain: string;
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

export enum HealthStatusLevel {
  Green = 0,
  Yellow = 1,
  Red = 2,
}

export enum HealthStatusTargetType {
  Series = 0,
  Provider = 1,
}

export interface SeriesHealth {
  id: string;
  title: string;
  thumbnailUrl?: string;
  level: HealthStatusLevel;
  message: string;
  lastChapterDate?: string;
  daysWithoutRelease?: number;
  /// Release cadence in days (absolute value, always positive)
  releaseCadenceDays?: number;
  providers: SmallProviderHealth[];
}

export interface ProviderHealth {
  providerId: string;
  providerName: string;
  scanlator: string;
  language: string;
  level: HealthStatusLevel;
  message: string;
  lastErrorDate?: string;
  consecutiveErrors: number;
  isMihonInstalled: boolean;
  affectedSeries: SeriesHealth[];
}

export interface SmallProviderHealth {
  providerId: string;
  providerName: string;
  language: string;
  level: HealthStatusLevel;
}

export interface StatusSummary {
  totalYellowSeries: number;
  totalRedSeries: number;
  totalYellowProviders: number;
  totalRedProviders: number;
}

export interface ClearAlertRequest {
  targetType: HealthStatusTargetType;
  targetId: string;
}

export interface SetCadenceRequest {
  /// Cadence in days. Null = clear user override and let system recalculate.
  cadenceDays?: number | null;
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
  Version: number;
}

export interface SmallSeries {
  id?: string;
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

export interface ImportJobStatus {
  isRunning: boolean;
  isQueued: boolean;
  isActive: boolean;
  hasCompleted: boolean;
  hasFailed: boolean;
}

export type SetupJobStatusValue = 'Running' | 'Waiting' | 'Completed' | 'Failed' | null;

export interface SetupJobsStatus {
  scanLocalFiles: SetupJobStatusValue;
  installAdditionalExtensions: SetupJobStatusValue;
  searchProviders: SetupJobStatusValue;
  importSeries: SetupJobStatusValue;
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
  StatusCheck = 10,
  ScrobblerSync = 11,
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
  /// Release cadence in days (absolute value, always positive). Null = not yet determined.
  releaseCadenceDays?: number;
  category?: string;
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

/**
 * A selectable source for (re-)downloading a chapter.
 */
export interface ChapterSource {
  id: string;
  name: string;
}

/**
 * A single chapter in the unified, series-level chapter list. Chapters are merged across every
 * source so the UI can tell, per chapter, whether it is downloaded (and from which source) or
 * genuinely missing — independent of which provider happens to hold the file.
 */
export interface ChapterDetail {
  number?: number;
  name: string;
  downloaded: boolean;
  sourceProviderId?: string;
  sourceProviderName?: string;
  availableProviders: ChapterSource[];
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

/**
 * A distinct tag/genre available in the cached "Latest" cloud catalogue, with the
 * number of series that carry it. Used to populate the browse-screen tag filter.
 */
export interface LatestGenre {
  name: string;
  count: number;
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

export interface SeriesRenameResult {
  success: boolean;
  /** True when the series folder was renamed on disk. */
  folderRenamed: boolean;
  /** Relative storage path before the rename. */
  oldFolder: string;
  /** Relative storage path after the rename (unchanged if the folder was not renamed). */
  newFolder: string;
  /** Number of .cbz archives renamed to the canonical scheme. */
  filesRenamed: number;
  /** Number of archives that could not be renamed (e.g. target name already taken). */
  filesFailed: number;
  /** Optional human-readable note (e.g. why the folder rename was skipped). */
  message?: string;
}

// --- User Management Types ---

export interface User {
  id: string;
  username: string;
  avatarBase64?: string;
  avatarContentType?: string;
  level: UserLevel;
  opdsPath: string;
  createdAt: string;
  lastLoginAt?: string;
  isActive: boolean;
  hasPassword: boolean;
}

export enum UserLevel {
  User = 0,
  Manager = 1,
  Admin = 2,
  Owner = 3,
}

export interface CreateUserRequest {
  username: string;
  level: UserLevel;
}

export interface UpdateUserRequest {
  avatarBase64?: string;
  avatarContentType?: string;
  removeAvatar?: boolean;
  level?: UserLevel;
  isActive?: boolean;
}

export interface AuthStatus {
  authenticationEnabled: boolean;
  hasUsers: boolean;
  users?: User[];
}

export interface LoginRequest {
  username: string;
  password: string;
  rememberMe?: boolean;
}

export interface LoginResponse {
  token: string;
  user: User;
}

export interface SetPasswordRequest {
  username: string;
  token: string;
  password: string;
}

export interface InviteMessage {
  message: string;
  token: string;
  opdsPath: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface NeedsPasswordResponse {
  needsPassword: boolean;
}

// ── Scrobbler Types ──

export enum ScrobblerProvider {
  MyAnimeList = 0,
  AniList = 1,
  ComicVine = 2,
  Kitsu = 3,
  MangaDex = 4,
}

export interface ScrobblerConfig {
  provider: ScrobblerProvider;
  displayName: string;
  isEnabled: boolean;
  isConnected: boolean;
  autoSync: boolean;
  lastSyncAt?: string;
  lastUploadAt?: string;
  lastDownloadAt?: string;
}

export interface ScrobblerSearchResult {
  externalId: string;
  title: string;
  alternateTitles: string[];
  coverUrl?: string;
  type?: string;
  chapterCount?: number;
  status?: string;
  synopsis?: string;
  score?: number;
}

export interface SeriesMatchStatus {
  seriesId: string;
  seriesTitle: string;
  provider: ScrobblerProvider;
  mappingStatus: SeriesMappingStatus;
  externalSeriesId?: string;
  externalSeriesTitle?: string;
  externalCoverUrl?: string;
  matchScore?: number;
}

export enum SeriesMappingStatus {
  Unmatched = 0,
  AutoMatched = 1,
  UserConfirmed = 2,
  Ignored = 3,
}

export interface ScrobblerConfigUpdate {
  isEnabled?: boolean;
  autoSync?: boolean;
}

export interface OAuthCallbackRequest {
  provider: ScrobblerProvider;
  code: string;
  state: string;
  codeVerifier?: string;
}

export interface SeriesMatchSearchRequest {
  provider: ScrobblerProvider;
  query: string;
}

export interface ConfirmMatchRequest {
  seriesId: string;
  provider: ScrobblerProvider;
  externalSeriesId: string;
  externalSeriesTitle?: string;
}

export interface DisableLinkRequest {
  seriesId: string;
  provider: ScrobblerProvider;
}

export interface AutoMatchResult {
  autoMatched: number;
  leftUnmatched: number;
  totalSeries: number;
  suggestedMatches: SeriesMatchStatus[];
}

export interface SyncStatus {
  provider: ScrobblerProvider;
  lastSyncAt?: string;
  lastUploadAt?: string;
  lastDownloadAt?: string;
  seriesMatched: number;
  seriesUnmatched: number;
  seriesIgnored: number;
}

export interface OAuthAuthorizeResponse {
  authUrl: string;
  state: string;
}
