/**
 * Configuration utilities for API and SignalR connections
 */

export interface ApiConfig {
  baseUrl: string;
  isAbsolute: boolean;
}

/**
 * Determines the appropriate base URL for API and SignalR connections
 * 
 * Logic:
 * - In development: Always use http://localhost:5001
 * - In production: Use NEXT_PUBLIC_KAIZOKU_BACKEND_URL if set, otherwise use relative paths
 * 
 * @returns Configuration object with baseUrl and isAbsolute flag
 */
export function getApiConfig(): ApiConfig {
  // Only run in browser environment
  if (typeof window === 'undefined') {
    return {
      baseUrl: '',
      isAbsolute: false
    };
  }

  const isDevelopment = process.env.NODE_ENV === 'development';
  const backendUrl = process.env.NEXT_PUBLIC_KAIZOKU_BACKEND_URL;
  
  if (isDevelopment) {
    // In development, always use localhost:5001
    return {
      baseUrl: 'http://localhost:5001',
      isAbsolute: true
    };
  } else if (backendUrl && backendUrl.trim() !== '') {
    // In production, use backend URL if provided
    return {
      baseUrl:  '',
      isAbsolute: false
    };
  } else {
    // In production with no backend URL, use relative URLs
    return {
      baseUrl: '',
      isAbsolute: false
    };
  }
}

/**
 * Builds a complete URL for API endpoints
 * @param endpoint - The API endpoint (e.g., '/api/series')
 * @returns Complete URL
 */
export function buildApiUrl(endpoint: string): string {
  const config = getApiConfig();
  return config.baseUrl ? `${config.baseUrl}${endpoint}` : endpoint;
}

/**
 * Builds a complete URL for SignalR hub connections
 * @param hubPath - The hub path (e.g., '/progress')
 * @returns Complete URL
 */
export function buildSignalRUrl(hubPath: string): string {
  const config = getApiConfig();
  return config.baseUrl ? `${config.baseUrl}${hubPath}` : hubPath;
}
