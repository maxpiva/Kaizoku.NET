import type { ProgressState } from '../types';
import { buildSignalRUrl } from '../config';

/* eslint-disable @typescript-eslint/no-explicit-any */

export class ProgressHub {
  private connection: any = null;
  private listeners: ((progress: ProgressState) => void)[] = [];
  private isInitialized = false;
  private signalR: any = null;

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

      this.isInitialized = true;
    }
  }

  async startConnection(): Promise<void> {
    if (typeof window === 'undefined') {
      return;
    }

    await this.ensureConnection();

    const signalR = await this.loadSignalR();
    if (!signalR || !this.connection) return;

    if (this.connection.state === signalR.HubConnectionState.Disconnected) {
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
