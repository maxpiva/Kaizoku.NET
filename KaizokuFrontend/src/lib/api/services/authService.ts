import { apiClient } from '@/lib/api/client';
import { tokenStore } from '@/lib/auth-token-store';
import type {
  AuthResponse,
  AuthStatusResponse,
  LoginRequest,
  RegisterRequest,
  SetupRequest,
} from '@/lib/api/auth-types';

export const authService = {
  async login(data: LoginRequest): Promise<AuthResponse> {
    return apiClient.post<AuthResponse>('/api/auth/login', data);
  },

  async register(data: RegisterRequest): Promise<AuthResponse> {
    return apiClient.post<AuthResponse>('/api/auth/register', data);
  },

  async logout(): Promise<void> {
    const refreshToken = tokenStore.getRefreshToken();
    return apiClient.post<void>('/api/auth/logout', { refreshToken });
  },

  async refresh(refreshToken: string): Promise<AuthResponse> {
    return apiClient.post<AuthResponse>('/api/auth/refresh', { refreshToken });
  },

  async status(): Promise<AuthStatusResponse> {
    return apiClient.get<AuthStatusResponse>('/api/auth/status');
  },

  async setup(data: SetupRequest): Promise<AuthResponse> {
    return apiClient.post<AuthResponse>('/api/auth/setup', data);
  },
};
