// ─── Auth & Multi-User Types ─────────────────────────────────────────────────

export interface User {
  id: string;
  username: string;
  email: string;
  displayName: string;
  role: 'Admin' | 'User';
  avatarPath: string | null;
  isActive: boolean;
  createdAt: string;
  lastLoginAt: string | null;
}

export interface UserPermissions {
  canViewLibrary: boolean;
  canRequestSeries: boolean;
  canAddSeries: boolean;
  canEditSeries: boolean;
  canDeleteSeries: boolean;
  canManageDownloads: boolean;
  canViewQueue: boolean;
  canBrowseSources: boolean;
  canViewNSFW: boolean;
  canManageRequests: boolean;
  canManageJobs: boolean;
  canViewStatistics: boolean;
}

export interface UserPreferences {
  theme: string;
  defaultLanguage: string;
  cardSize: string;
  nsfwVisibility: string; // Backend sends string enum via JsonStringEnumConverter ("AlwaysHide" | "HideByDefault" | "Show")
}

export interface UserDetail extends User {
  permissions: UserPermissions;
  preferences: UserPreferences;
  updatedAt: string;
}

export interface AuthResponse {
  accessToken: string;
  refreshToken: string;
  expiresAt: string;
  user: UserDetail;
  /** True when the user's password doesn't meet the current policy and must be changed. */
  requiresPasswordChange?: boolean;
}

export interface AuthStatusResponse {
  requiresSetup: boolean;
}

export interface InviteLink {
  id: string;
  code: string;
  createdByUserId: string;
  createdByUsername: string;
  expiresAt: string;
  maxUses: number;
  usedCount: number;
  permissionPresetId: string | null;
  permissionPresetName: string | null;
  isActive: boolean;
}

export interface InviteValidation {
  isValid: boolean;
  reason?: string;
  permissionPresetName?: string | null;
}

// Backend sends permissions nested: { id, name, isDefault, createdByUserId, permissions: { ... } }
export interface PermissionPreset {
  id: string;
  name: string;
  isDefault: boolean;
  createdByUserId: string;
  permissions: UserPermissions;
}

export interface MangaRequest {
  id: string;
  title: string;
  description: string | null;
  thumbnailUrl: string | null;
  providerData: string | null;
  status: 'Pending' | 'Approved' | 'Denied' | 'Cancelled';
  requestedByUsername: string;
  requestedByUserId: string;
  reviewedByUserId: string | null;
  reviewedByUsername: string | null;
  reviewNote: string | null;
  reviewedAt: string | null;
  createdAt: string;
}

export interface PendingRequestCount {
  count: number;
}

// ─── Request Bodies ───────────────────────────────────────────────────────────

export interface LoginRequest {
  usernameOrEmail: string;
  password: string;
  rememberMe?: boolean;
}

export interface RegisterRequest {
  username: string;
  email: string;
  password: string;
  displayName: string;
  inviteCode: string;
}

export interface SetupRequest {
  username: string;
  email: string;
  password: string;
  displayName: string;
}

export interface CreateUserRequest {
  username: string;
  email: string;
  password: string;
  displayName: string;
  role: 'Admin' | 'User';
  permissions?: UserPermissions;
}

export interface UpdateUserRequest {
  displayName?: string;
  email?: string;
  role?: 'Admin' | 'User';
  isActive?: boolean;
}

export interface UpdatePermissionsRequest extends Partial<UserPermissions> {}

export interface ResetPasswordRequest {
  newPassword: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface UpdateProfileRequest {
  displayName?: string;
}

export interface UpdatePreferencesRequest {
  theme?: string;
  defaultLanguage?: string;
  cardSize?: string;
  nsfwVisibility?: string;
}

export interface CreateInviteRequest {
  expiresInDays: number;
  maxUses: number;
  permissionPresetId?: string;
}

export interface CreateRequestRequest {
  title: string;
  description?: string;
  thumbnailUrl?: string;
  providerData: string;
}

export interface ApproveRequestRequest {
  seriesData?: unknown;
  reviewNote?: string;
}

export interface DenyRequestRequest {
  reviewNote?: string;
}

export interface CreatePresetRequest {
  name: string;
  permissions: UserPermissions;
}

export type UpdatePresetRequest = {
  name?: string;
  permissions?: Partial<UserPermissions>;
};
