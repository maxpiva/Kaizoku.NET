import { getApiConfig } from './config';
import { tokenStore, triggerLogout } from '@/lib/auth-token-store';

// Track in-flight refresh promise to avoid duplicate refreshes.
// Shared across both the API client and SignalR hub to prevent
// concurrent refresh requests from consuming the same refresh token.
let refreshPromise: Promise<string | null> | null = null;

async function doRefresh(baseUrl: string): Promise<string | null> {
  const refreshToken = tokenStore.getRefreshToken();
  if (!refreshToken) return null;

  try {
    const url = baseUrl ? `${baseUrl}/api/auth/refresh` : '/api/auth/refresh';
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify({ refreshToken }),
    });

    if (!res.ok) return null;

    const text = await res.text();
    if (!text) return null;

    const data = JSON.parse(text) as { accessToken?: string; refreshToken?: string };
    if (!data.accessToken) return null;

    tokenStore.setTokens(
      data.accessToken,
      data.refreshToken ?? refreshToken,
      tokenStore.isRemembered()
    );
    return data.accessToken;
  } catch {
    return null;
  }
}

class KaizokuApiClient {
  private baseUrl: string;

  constructor(baseUrl = '') {
    this.baseUrl = baseUrl;
  }

  private async request<T>(
    endpoint: string,
    options: RequestInit = {},
    _retry = true
  ): Promise<T> {
    const url = this.baseUrl ? `${this.baseUrl}${endpoint}` : endpoint;

    // Determine if we're sending FormData
    const isFormData = options.body instanceof FormData;

    // Attach bearer token if available
    const accessToken = tokenStore.getAccessToken();
    const authHeaders: Record<string, string> = accessToken
      ? { Authorization: `Bearer ${accessToken}` }
      : {};

    const response = await fetch(url, {
      headers: {
        ...(isFormData ? {} : { 'Content-Type': 'application/json' }),
        ...authHeaders,
        ...options.headers,
      },
      credentials: 'include',
      ...options,
    });

    // Handle 401 — attempt token refresh once
    if (response.status === 401 && _retry) {
      // Deduplicate concurrent refresh attempts
      if (!refreshPromise) {
        refreshPromise = doRefresh(this.baseUrl).finally(() => {
          refreshPromise = null;
        });
      }
      const newToken = await refreshPromise;

      if (!newToken) {
        // Refresh failed — force logout
        triggerLogout();
        throw new Error('Session expired. Please log in again.');
      }

      // Retry original request with new token
      return this.request<T>(endpoint, options, false);
    }

    if (!response.ok) {
      // Try to parse error message from body
      let errorMsg = `API Error: ${response.status} ${response.statusText}`;
      try {
        const errText = await response.text();
        if (errText) {
          const errJson = JSON.parse(errText) as { message?: string; title?: string };
          errorMsg = errJson.message ?? errJson.title ?? errorMsg;
        }
      } catch {
        // ignore parse errors
      }
      throw new Error(errorMsg);
    }

    // Handle empty responses properly
    const contentLength = response.headers.get('content-length');
    const contentType = response.headers.get('content-type');

    if (contentLength === '0' || response.status === 204) {
      return undefined as T;
    }

    const isJsonResponse = contentType?.includes('application/json');

    try {
      const text = await response.text();

      if (!text || text.trim() === '') {
        return undefined as T;
      }

      const looksLikeJson = text.trim().startsWith('{') || text.trim().startsWith('[');
      if (isJsonResponse ?? looksLikeJson) {
        try {
          const result = JSON.parse(text) as { data?: T } | T;
          return result && typeof result === 'object' && 'data' in result && result.data !== undefined
            ? result.data
            : (result as T);
        } catch {
          if (text.trim() === '{}' || text.trim() === 'null') {
            return undefined as T;
          }
          throw new Error(`Invalid JSON response: ${text}`);
        }
      }

      return text as T;
    } catch (error) {
      if (error instanceof Error && error.message.includes('JSON')) {
        throw error;
      }
      if (response.status === 200) {
        return undefined as T;
      }
      throw new Error(`Failed to process response: ${error instanceof Error ? error.message : 'Unknown error'}`);
    }
  }

  async get<T>(endpoint: string, options?: { signal?: AbortSignal }): Promise<T> {
    return this.request<T>(endpoint, { method: 'GET', signal: options?.signal });
  }

  async post<T>(endpoint: string, data?: unknown, _options?: { params?: Record<string, unknown> }): Promise<T> {
    return this.request<T>(endpoint, {
      method: 'POST',
      body: data instanceof FormData ? data : (data ? JSON.stringify(data) : undefined),
    });
  }

  async put<T>(endpoint: string, data?: unknown): Promise<T> {
    return this.request<T>(endpoint, {
      method: 'PUT',
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  async patch<T>(endpoint: string, data?: unknown): Promise<T> {
    return this.request<T>(endpoint, {
      method: 'PATCH',
      body: JSON.stringify(data ?? {}),
    });
  }

  async delete<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: 'DELETE' });
  }
}

export const apiClient = new KaizokuApiClient(getApiConfig().baseUrl);

/**
 * Shared, deduplicated token refresh. Used by both the API client and
 * SignalR hub so they don't race each other on the same refresh token.
 * Returns the new access token, or null if refresh failed.
 */
export function refreshAccessToken(): Promise<string | null> {
  if (!refreshPromise) {
    refreshPromise = doRefresh(getApiConfig().baseUrl).finally(() => {
      refreshPromise = null;
    });
  }
  return refreshPromise;
}
