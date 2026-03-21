import type { ProgressState } from '../types';
import { buildSignalRUrl } from '../config';
import { tokenStore } from '@/lib/auth-token-store';
import { refreshAccessToken } from '../client';

/* eslint-disable @typescript-eslint/no-explicit-any */

export class ProgressHub {
  private connection: any = null;
  private listeners: ((progress: ProgressState) => void)[] = [];
  private isInitialized = false;
  private isInitializing = false; // Guard against concurrent ensureConnection calls
  private signalR: any = null;
  private healthCheckInterval: NodeJS.Timeout | null = null;

  private async loadSignalR(): Promise<any> {
    if (typeof window === 'undefined') {
      return null;
    }

    if (!this.signalR) {
      this.signalR = await import('@microsoft/signalr');
    }
    return this.signalR;
  }

  /**
   * Ensures a fresh access token is available for SignalR connections.
   * Called by the HubConnectionBuilder on every negotiate and reconnect.
   *
   * If the current token looks expired (JWT exp claim check), attempts
   * a refresh before returning the token. This prevents reconnect loops
   * where SignalR keeps getting 401 on stale tokens.
   */
  private async getAccessToken(): Promise<string> {
    let token = tokenStore.getAccessToken();

    if (token && this.isTokenExpiringSoon(token)) {
      const refreshed = await this.tryRefreshToken();
      if (refreshed) {
        token = tokenStore.getAccessToken();
      }
    }

    return token ?? '';
  }

  /**
   * Checks if a JWT token is expired or will expire within 60 seconds.
   */
  private isTokenExpiringSoon(token: string): boolean {
    try {
      const parts = token.split('.');
      const encodedPayload = parts[1];
      if (!encodedPayload) return false;
      const payload = JSON.parse(atob(encodedPayload)) as { exp?: number };
      const exp = payload.exp;
      if (!exp) return false;
      // Consider expired if within 60s of expiration
      return Date.now() >= (exp - 60) * 1000;
    } catch {
      return false;
    }
  }

  /**
   * Attempts to refresh the access token using the shared, deduplicated
   * refresh from the API client. This prevents two concurrent refresh
   * requests (API client + SignalR) from consuming the same refresh token.
   */
  private async tryRefreshToken(): Promise<boolean> {
    const newToken = await refreshAccessToken();
    return newToken !== null;
  }

  private async ensureConnection(): Promise<void> {
    if (typeof window === 'undefined') {
      return;
    }    if (!this.isInitialized && !this.isInitializing) {
      this.isInitializing = true;
      const signalR = await this.loadSignalR();
      if (!signalR) return;

      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(buildSignalRUrl('/progress'), {
          accessTokenFactory: () => this.getAccessToken(),
        })
        .withAutomaticReconnect()
        .build();      this.connection.on('Progress', (progress: ProgressState) => {
        this.listeners.forEach(listener => listener(progress));
      });

      // Add connection state event handlers
      this.connection.onreconnecting((error: any) => {
        console.log('SignalR reconnecting...', error);
      });

      this.connection.onreconnected((connectionId: string) => {
        console.log('SignalR reconnected:', connectionId);
      });

      this.connection.onclose(async (error: any) => {
        console.log('SignalR connection closed:', error);
        // If closed unexpectedly (not by us calling stop), try to refresh
        // token and reconnect after a short delay
        if (error) {
          const refreshed = await this.tryRefreshToken();
          if (refreshed) {
            setTimeout(() => void this.reconnectAfterAuthRefresh(), 2000);
          }
        }
      });

      this.setupVisibilityHandling();
      this.isInitialized = true;
      this.isInitializing = false;
    }
  }

  private async reconnectAfterAuthRefresh(): Promise<void> {
    const signalR = await this.loadSignalR();
    if (!signalR || !this.connection) return;

    if (this.connection.state === signalR.HubConnectionState.Disconnected) {
      try {
        await this.connection.start();
        console.log('SignalR reconnected after token refresh');
      } catch (err) {
        console.error('SignalR reconnect after refresh failed:', err);
      }
    }
  }

  private setupVisibilityHandling(): void {
    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', () => {
        if (!document.hidden) {
          // Tab became visible - check connection
          void this.handleTabVisible();
        }
      });
    }
  }

  private async handleTabVisible(): Promise<void> {
    const signalR = await this.loadSignalR();
    if (!signalR || !this.connection) return;

    // If disconnected or reconnecting failed, restart
    if (this.connection.state === signalR.HubConnectionState.Disconnected) {
      try {
        await this.connection.start();
      } catch (err) {
        console.error('Failed to restart connection on tab visible:', err);
      }
    }
  }

  async ensureConnected(): Promise<boolean> {
    const signalR = await this.loadSignalR();
    if (!signalR || !this.connection) return false;

    if (this.connection.state === signalR.HubConnectionState.Connected) {
      return true;
    }

    try {
      await this.startConnection();
      return this.connection.state === signalR.HubConnectionState.Connected;
    } catch {
      return false;
    }
  }

  async startConnection(): Promise<void> {
    if (typeof window === 'undefined') {
      return;
    }

    await this.ensureConnection();

    const signalR = await this.loadSignalR();
    if (!signalR || !this.connection) return;

    // Handle all non-connected states
    if (this.connection.state !== signalR.HubConnectionState.Connected) {
      try {
        await this.connection.start();
      } catch (err) {
        console.error('SignalR Connection Error:', err);
        throw err;
      }
    }
  }

  async stopConnection(): Promise<void> {
    const signalR = await this.loadSignalR();
    if (!signalR || !this.connection) return;

    if (this.connection.state === signalR.HubConnectionState.Connected) {
      try {
        await this.connection.stop();
      } catch (err) {
        console.error('SignalR Disconnection Error:', err);
      }
    }
  }

  startHealthCheck(): void {
    if (this.healthCheckInterval) return;
    
    this.healthCheckInterval = setInterval(async () => {
      if (typeof document !== 'undefined' && !document.hidden) {
        await this.ensureConnected();
      }
    }, 30000); // Check every 30 seconds when tab is visible
  }

  stopHealthCheck(): void {
    if (this.healthCheckInterval) {
      clearInterval(this.healthCheckInterval);
      this.healthCheckInterval = null;
    }
  }

  onProgress(callback: (progress: ProgressState) => void): () => void {
    this.listeners.push(callback);

    return () => {
      const index = this.listeners.indexOf(callback);
      if (index > -1) {
        this.listeners.splice(index, 1);
      }
    };
  }

  dispose(): void {
    this.listeners = [];
    this.stopHealthCheck();
    if (this.connection) {
      void this.stopConnection();
    }
  }
}

let progressHubInstance: ProgressHub | null = null;

export const getProgressHub = (): ProgressHub => {
  if (!progressHubInstance) {
    progressHubInstance = new ProgressHub();
  }
  return progressHubInstance;
};
