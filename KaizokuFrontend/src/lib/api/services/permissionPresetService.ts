import { apiClient } from '@/lib/api/client';
import type { PermissionPreset, CreatePresetRequest, UpdatePresetRequest } from '@/lib/api/auth-types';

export const permissionPresetService = {
  async listPresets(): Promise<PermissionPreset[]> {
    return apiClient.get<PermissionPreset[]>('/api/permission-presets');
  },

  async createPreset(data: CreatePresetRequest): Promise<PermissionPreset> {
    return apiClient.post<PermissionPreset>('/api/permission-presets', data);
  },

  async updatePreset(id: string, data: UpdatePresetRequest): Promise<PermissionPreset> {
    return apiClient.patch<PermissionPreset>(`/api/permission-presets/${id}`, data);
  },

  async deletePreset(id: string): Promise<void> {
    return apiClient.delete<void>(`/api/permission-presets/${id}`);
  },

  async setDefaultPreset(id: string): Promise<PermissionPreset> {
    return apiClient.post<PermissionPreset>(`/api/permission-presets/${id}/set-default`);
  },

  async clearDefaultPreset(): Promise<void> {
    return apiClient.post<void>('/api/permission-presets/clear-default');
  },
};
