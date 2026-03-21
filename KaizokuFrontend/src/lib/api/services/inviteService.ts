import { apiClient } from '@/lib/api/client';
import type { InviteLink, InviteValidation, CreateInviteRequest } from '@/lib/api/auth-types';

export const inviteService = {
  async createInvite(data: CreateInviteRequest): Promise<InviteLink> {
    return apiClient.post<InviteLink>('/api/invites', data);
  },

  async listInvites(): Promise<InviteLink[]> {
    return apiClient.get<InviteLink[]>('/api/invites');
  },

  async revokeInvite(id: string): Promise<void> {
    return apiClient.delete<void>(`/api/invites/${id}`);
  },

  async validateInvite(code: string): Promise<InviteValidation> {
    return apiClient.get<InviteValidation>(`/api/invites/validate/${code}`);
  },
};
