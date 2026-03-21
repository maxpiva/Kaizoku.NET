import { apiClient } from '@/lib/api/client';
import type {
  UserDetail,
  UserPreferences,
  CreateUserRequest,
  UpdateUserRequest,
  UpdatePermissionsRequest,
  ResetPasswordRequest,
  ChangePasswordRequest,
  UpdateProfileRequest,
  UpdatePreferencesRequest,
} from '@/lib/api/auth-types';

export const userService = {
  // ─── Admin: all users ────────────────────────────────────────────────
  async listUsers(): Promise<UserDetail[]> {
    return apiClient.get<UserDetail[]>('/api/users');
  },

  async getUser(id: string): Promise<UserDetail> {
    return apiClient.get<UserDetail>(`/api/users/${id}`);
  },

  async createUser(data: CreateUserRequest): Promise<UserDetail> {
    return apiClient.post<UserDetail>('/api/users', data);
  },

  async updateUser(id: string, data: UpdateUserRequest): Promise<UserDetail> {
    return apiClient.patch<UserDetail>(`/api/users/${id}`, data);
  },

  async deleteUser(id: string): Promise<void> {
    return apiClient.delete<void>(`/api/users/${id}`);
  },

  async updatePermissions(id: string, data: UpdatePermissionsRequest): Promise<void> {
    // Backend returns { message: string }, not the permissions object
    return apiClient.patch<void>(`/api/users/${id}/permissions`, data);
  },

  async resetPassword(id: string, data: ResetPasswordRequest): Promise<void> {
    return apiClient.post<void>(`/api/users/${id}/reset-password`, data);
  },

  async disableUser(id: string): Promise<void> {
    return apiClient.post<void>(`/api/users/${id}/disable`);
  },

  async enableUser(id: string): Promise<void> {
    return apiClient.post<void>(`/api/users/${id}/enable`);
  },

  // ─── Current user ─────────────────────────────────────────────────────
  async getCurrentUser(): Promise<UserDetail> {
    return apiClient.get<UserDetail>('/api/users/me');
  },

  async updateProfile(data: UpdateProfileRequest): Promise<UserDetail> {
    return apiClient.patch<UserDetail>('/api/users/me', data);
  },

  async changePassword(data: ChangePasswordRequest): Promise<void> {
    return apiClient.patch<void>('/api/users/me/password', data);
  },

  async getPreferences(): Promise<UserPreferences> {
    return apiClient.get<UserPreferences>('/api/users/me/preferences');
  },

  async updatePreferences(data: UpdatePreferencesRequest): Promise<UserPreferences> {
    return apiClient.patch<UserPreferences>('/api/users/me/preferences', data);
  },

  async uploadAvatar(file: File): Promise<void> {
    const formData = new FormData();
    formData.append('file', file);
    return apiClient.post<void>('/api/users/me/avatar', formData);
  },

  getAvatarUrl(id: string): string {
    // Build URL using the same config logic
    if (typeof window !== 'undefined' && process.env.NODE_ENV === 'development') {
      return `http://127.0.0.1:9833/api/users/avatar/${id}`;
    }
    return `/api/users/avatar/${id}`;
  },
};
