import { apiClient } from '@/lib/api/client';
import { type Settings } from '@/lib/api/types';

export const settingsService = {
  async getSettings(): Promise<Settings> {
    const data = await apiClient.get<Settings>('/api/settings');
    return {
      ...data,
    };
  },

  async getAvailableLanguages(): Promise<string[]> {
    return apiClient.get<string[]>('/api/settings/languages');
  },

  async updateSettings(settings: Settings): Promise<void> {
    const settingsPayload = {
      ...settings
    };

    return apiClient.put<void>('/api/settings', settingsPayload);
  },
};
