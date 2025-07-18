// Types for queue management
export interface QueueItem {
  id: string;
  mangaId: number;
  chapterIndex: number;
  seriesTitle: string;
  chapterTitle: string;
  status: 'queued' | 'downloading' | 'completed' | 'error';
  progress?: number;
  thumbnailUrl?: string;
}

export interface QueueService {
  getQueueItems(): Promise<QueueItem[]>;
  removeFromQueue(id: string): Promise<boolean>;
  clearQueue(): Promise<boolean>;
}

// Mock service for demo purposes
class MockQueueService implements QueueService {
  private mockData: QueueItem[] = [];

  async getQueueItems(): Promise<QueueItem[]> {
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 500));
    return [...this.mockData];
  }

  async removeFromQueue(id: string): Promise<boolean> {
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 200));
    const index = this.mockData.findIndex(item => item.id === id);
    if (index > -1) {
      this.mockData.splice(index, 1);
      return true;
    }
    return false;
  }

  async clearQueue(): Promise<boolean> {
    // Simulate API delay
    await new Promise(resolve => setTimeout(resolve, 300));
    this.mockData = [];
    return true;
  }
}

export const queueService = new MockQueueService();
