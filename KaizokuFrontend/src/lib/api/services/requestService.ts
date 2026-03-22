import { apiClient } from '@/lib/api/client';
import type {
  MangaRequest,
  PendingRequestCount,
  CreateRequestRequest,
  ApproveRequestRequest,
  DenyRequestRequest,
} from '@/lib/api/auth-types';

export const requestService = {
  async createRequest(data: CreateRequestRequest): Promise<MangaRequest> {
    return apiClient.post<MangaRequest>('/api/requests', data);
  },

  async listRequests(): Promise<MangaRequest[]> {
    return apiClient.get<MangaRequest[]>('/api/requests');
  },

  async getPendingCount(): Promise<PendingRequestCount> {
    return apiClient.get<PendingRequestCount>('/api/requests/pending-count');
  },

  async approveRequest(id: string, data?: ApproveRequestRequest): Promise<MangaRequest> {
    return apiClient.patch<MangaRequest>(`/api/requests/${id}/approve`, data);
  },

  async denyRequest(id: string, data?: DenyRequestRequest): Promise<MangaRequest> {
    return apiClient.patch<MangaRequest>(`/api/requests/${id}/deny`, data);
  },

  async cancelRequest(id: string): Promise<MangaRequest> {
    return apiClient.patch<MangaRequest>(`/api/requests/${id}/cancel`);
  },
};
