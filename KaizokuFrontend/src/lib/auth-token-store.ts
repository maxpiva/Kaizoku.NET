/**
 * Module-level token store — works outside React components (needed for
 * the API client interceptor which runs at fetch time, not render time).
 */

const TOKEN_KEY = 'kzk_access_token';
const REFRESH_KEY = 'kzk_refresh_token';
const REMEMBER_KEY = 'kzk_remember_me';

export const tokenStore = {
  getAccessToken(): string | null {
    if (typeof window === 'undefined') return null;
    return localStorage.getItem(TOKEN_KEY);
  },

  getRefreshToken(): string | null {
    if (typeof window === 'undefined') return null;
    return localStorage.getItem(REFRESH_KEY);
  },

  setTokens(accessToken: string, refreshToken: string, rememberMe = false): void {
    if (typeof window === 'undefined') return;
    localStorage.setItem(TOKEN_KEY, accessToken);
    localStorage.setItem(REFRESH_KEY, refreshToken);
    localStorage.setItem(REMEMBER_KEY, rememberMe ? 'true' : 'false');
  },

  clearTokens(): void {
    if (typeof window === 'undefined') return;
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(REFRESH_KEY);
    localStorage.removeItem(REMEMBER_KEY);
  },

  isRemembered(): boolean {
    if (typeof window === 'undefined') return false;
    return localStorage.getItem(REMEMBER_KEY) === 'true';
  },
};

// Callbacks so the client can trigger auth context actions
type LogoutCallback = () => void;
let logoutCallback: LogoutCallback | null = null;

export function registerLogoutCallback(cb: LogoutCallback) {
  logoutCallback = cb;
}

export function triggerLogout() {
  tokenStore.clearTokens();
  logoutCallback?.();
}
