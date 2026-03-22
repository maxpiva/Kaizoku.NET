import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { inviteService } from '@/lib/api/services/inviteService';
import type { CreateInviteRequest } from '@/lib/api/auth-types';

export const useInvites = () => {
  return useQuery({
    queryKey: ['invites'],
    queryFn: () => inviteService.listInvites(),
    staleTime: 15_000,
  });
};

export const useCreateInvite = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateInviteRequest) => inviteService.createInvite(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['invites'] });
    },
  });
};

export const useRevokeInvite = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => inviteService.revokeInvite(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['invites'] });
    },
  });
};

export const useValidateInvite = (code: string) => {
  return useQuery({
    queryKey: ['invites', 'validate', code],
    queryFn: () => inviteService.validateInvite(code),
    enabled: !!code,
    retry: false,
    staleTime: 60_000,
  });
};
