import { apiClient } from '@/lib/api/client';
import type { Provider, ProviderPreferences, ProviderHealthResult } from '../types';

export const providerService = {
  /**
   * Gets a list of all available providers (installed and available to install)
   */
  async getProviders(): Promise<Provider[]> {
    return apiClient.get<Provider[]>('/api/provider/list');
  },

  /**
   * Installs a provider by package name
   */
  async installProvider(pkgName: string, options?: { repoName?: string; force?: boolean }): Promise<{ message: string }> {
    const params = new URLSearchParams();
    if (options?.repoName) params.append('repoName', options.repoName);
    if (options?.force !== undefined) params.append('force', String(options.force));
    const query = params.toString();
    return apiClient.post<{ message: string }>(`/api/provider/install/${pkgName}${query ? `?${query}` : ''}`, null);
  },

  /**
   * Installs a provider from an uploaded file
   */
  async installProviderFromFile(file: File, options?: { force?: boolean }): Promise<string> {
    const formData = new FormData();
    formData.append('file', file);

    const params = new URLSearchParams();
    if (options?.force !== undefined) params.append('force', String(options.force));
    const query = params.toString();

    return apiClient.post<string>(`/api/provider/install/file${query ? `?${query}` : ''}`, formData);
  },

  /**
   * Uninstalls a provider by package name
   */
  async uninstallProvider(pkgName: string): Promise<{ message: string }> {
    return apiClient.post<{ message: string }>(`/api/provider/uninstall/${pkgName}`, null);
  },

  /**
   * Gets provider preferences by package name
   */
  async getProviderPreferences(pkgName: string): Promise<ProviderPreferences> {
    return apiClient.get<ProviderPreferences>(`/api/provider/preferences/${pkgName}`);
  },

  /**
   * Sets provider preferences
   */
  async setProviderPreferences(preferences: ProviderPreferences): Promise<void> {
    return apiClient.post<void>('/api/provider/preferences', preferences);
  },

  /**
   * Checks health of a single provider by performing a test search
   */
  async checkProviderHealth(mihonProviderId: string): Promise<ProviderHealthResult> {
    return apiClient.post<ProviderHealthResult>(
      `/api/provider/health-check/${encodeURIComponent(mihonProviderId)}`,
      null
    );
  },

  /**
   * Checks health of all sources in a specific extension package
   */
  async checkPackageHealth(pkgName: string): Promise<ProviderHealthResult[]> {
    return apiClient.post<ProviderHealthResult[]>(
      `/api/provider/health-check/package/${encodeURIComponent(pkgName)}`,
      null
    );
  },

  /**
   * Checks health of all installed/enabled providers
   */
  async checkAllProvidersHealth(): Promise<ProviderHealthResult[]> {
    return apiClient.post<ProviderHealthResult[]>('/api/provider/health-check', null);
  },
};
