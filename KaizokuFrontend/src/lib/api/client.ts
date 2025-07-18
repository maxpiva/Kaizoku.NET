import { getApiConfig } from './config';

class KaizokuApiClient {
  private baseUrl: string;
  constructor(baseUrl = '') {
    // Use relative URLs when no baseUrl is provided (production mode)
    // Use absolute URL when baseUrl is provided (development mode)
    this.baseUrl = baseUrl;
  }  private async request<T>(
    endpoint: string,
    options: RequestInit = {}
  ): Promise<T> {
    const url = this.baseUrl ? `${this.baseUrl}${endpoint}` : endpoint;
    
    // Determine if we're sending FormData
    const isFormData = options.body instanceof FormData;
    
    const response = await fetch(url, {
      headers: {
        // Only set Content-Type for non-FormData requests
        // FormData requests need the browser to set Content-Type with boundary
        ...(isFormData ? {} : { 'Content-Type': 'application/json' }),
        ...options.headers,
      }, credentials: 'include', // Include cookies for session management
      ...options,
    });

    if (!response.ok) {
      throw new Error(`API Error: ${response.status} ${response.statusText}`);
    }    // Handle empty responses properly
    const contentLength = response.headers.get('content-length');
    const contentType = response.headers.get('content-type');
    
    // Check for explicitly empty responses
    if (contentLength === '0' || response.status === 204) {
      return undefined as T;
    }

    // For responses that should contain JSON, check if content-type indicates JSON
    const isJsonResponse = contentType?.includes('application/json');
    
    try {
      // Try to get response text first
      const text = await response.text();
      
      // If no text content or only whitespace, return undefined
      if (!text || text.trim() === '') {
        return undefined as T;
      }      // If response has content, try to parse as JSON
      if (isJsonResponse ?? (text.trim().startsWith('{') ?? text.trim().startsWith('['))) {
        try {
          const result = JSON.parse(text) as { data?: T } | T;
          return result && typeof result === 'object' && 'data' in result && result.data !== undefined 
            ? result.data 
            : result as T;
        } catch (jsonError) {
          // JSON parsing failed, but we have text content
          // For void API endpoints, this might still be valid if text is minimal
          if (text.trim() === '{}' || text.trim() === 'null') {
            return undefined as T;
          }
          throw new Error(`Invalid JSON response: ${text}`);
        }
      }
      
      // Non-JSON response with content - return as string cast to T
      return text as T;
      
    } catch (error) {
      // Handle fetch/text reading errors
      if (error instanceof Error && error.message.includes('JSON')) {
        throw error;
      }
      
      // For other errors, assume it's a void response if status is 200
      if (response.status === 200) {
        return undefined as T;
      }
      
      throw new Error(`Failed to process response: ${error instanceof Error ? error.message : 'Unknown error'}`);
    }
  }

  async get<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: 'GET' });
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
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  async delete<T>(endpoint: string): Promise<T> {
    return this.request<T>(endpoint, { method: 'DELETE' });
  }
}

export const apiClient = new KaizokuApiClient(getApiConfig().baseUrl);
