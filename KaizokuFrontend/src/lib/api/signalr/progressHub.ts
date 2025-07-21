import type { ProgressState } from '../types';
import { buildSignalRUrl } from '../config';

/* eslint-disable @typescript-eslint/no-explicit-any */

export class ProgressHub {
  private connection: any = null;
  private listeners: ((progress: ProgressState) => void)[] = [];
  private isInitialized = false;
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

  private async ensureConnection(): Promise<void> {
    if (typeof window === 'undefined') {
      return;
    }    if (!this.isInitialized) {
      const signalR = await this.loadSignalR();
      if (!signalR) return;

      this.connection = new signalR.HubConnectionBuilder()
        .withUrl(buildSignalRUrl('/progress'))
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
