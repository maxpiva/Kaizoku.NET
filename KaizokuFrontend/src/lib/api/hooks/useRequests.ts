import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { requestService } from '@/lib/api/services/requestService';
import type {
  CreateRequestRequest,
  ApproveRequestRequest,
  DenyRequestRequest,
} from '@/lib/api/auth-types';

export const useRequests = () => {
  return useQuery({
    queryKey: ['requests'],
    queryFn: () => requestService.listRequests(),
    staleTime: 15_000,
  });
};

export const usePendingRequestCount = () => {
  return useQuery({
    queryKey: ['requests', 'pending-count'],
    queryFn: () => requestService.getPendingCount(),
    staleTime: 15_000,
    refetchInterval: 30_000,
  });
};

export const useCreateRequest = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateRequestRequest) => requestService.createRequest(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests'] });
    },
  });
};

export const useApproveRequest = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data?: ApproveRequestRequest }) =>
      requestService.approveRequest(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests'] });
    },
  });
};

export const useDenyRequest = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data?: DenyRequestRequest }) =>
      requestService.denyRequest(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests'] });
    },
  });
};

export const useCancelRequest = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => requestService.cancelRequest(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests'] });
    },
  });
};
