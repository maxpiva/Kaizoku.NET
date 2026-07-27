import type { ProgressState, DiscoverySearchEvent } from '../types';
import { buildSignalRUrl } from '../config';

/* eslint-disable @typescript-eslint/no-explicit-any */

export class ProgressHub {
  private connection: any = null;
  private listeners: ((progress: ProgressState) => void)[] = [];
  private discoveryListeners: ((event: DiscoverySearchEvent) => void)[] = [];
  private isInitialized = false;
  private signalR: any = null;
  private healthCheckInterval: NodeJS.Timeout | null = null;
  private startPromise: Promise<void> | null = null;

  private async loadSignalR(): Promise<any> {
    if (typeof window === 'undefined') {
      return null;
    }
    
    if (!this.signalR) {
      this.signalR = await import('@microsoft/signalr');
    }
    return this.signalR;
  }

  private async ensureConnection(): Promise<void> {
    if (typeof window === 'undefined') {
      return;
    }
    
    if (!this.isInitialized) {
      const signalR = await this.loadSignalR();
      if (!signalR) return;

      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(buildSignalRUrl('/progress'))
        .withAutomaticReconnect()
        .build();

      this.connection.on('Progress', (progress: ProgressState) => {
        this.listeners.forEach(listener => listener(progress));
      });

      this.connection.on('DiscoverySearch', (event: DiscoverySearchEvent) => {
        this.discoveryListeners.forEach(listener => listener(event));
      });

      // Add connection state event handlers
      this.connection.onreconnecting((error: any) => {
        console.log('SignalR reconnecting...', error);
      });

      this.connection.onreconnected((connectionId: string) => {
        console.log('SignalR reconnected:', connectionId);
      });

      this.connection.onclose((error: any) => {
        console.log('SignalR connection closed:', error);
      });

      this.setupVisibilityHandling();
      this.isInitialized = true;
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
        await this.startConnection();
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

    // If already connected, nothing to do
    if (this.connection.state === signalR.HubConnectionState.Connected) {
      return;
    }

    // If a start is already in progress, wait for it instead of starting another
    if (this.startPromise) {
      return this.startPromise;
    }

    this.startPromise = this.connection.start()
      .then(() => {
        this.startPromise = null;
      })
      .catch((err: any) => {
        this.startPromise = null;
        console.error('SignalR Connection Error:', err);
        throw err;
      });

    return this.startPromise!;
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

  /** Subscribe to streaming discovery-search events ("DiscoverySearch" on the same hub). */
  onDiscovery(callback: (event: DiscoverySearchEvent) => void): () => void {
    this.discoveryListeners.push(callback);

    return () => {
      const index = this.discoveryListeners.indexOf(callback);
      if (index > -1) {
        this.discoveryListeners.splice(index, 1);
      }
    };
  }

  dispose(): void {
    this.listeners = [];
    this.discoveryListeners = [];
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
