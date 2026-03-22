import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { authService } from '@/lib/api/services/authService';
import type { LoginRequest, RegisterRequest, SetupRequest } from '@/lib/api/auth-types';

export const useAuthStatus = () => {
  return useQuery({
    queryKey: ['auth', 'status'],
    queryFn: () => authService.status(),
    retry: false,
    staleTime: 30_000,
  });
};

export const useLogin = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: LoginRequest) => authService.login(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['auth'] });
      queryClient.invalidateQueries({ queryKey: ['users', 'me'] });
    },
  });
};

export const useRegister = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: RegisterRequest) => authService.register(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['auth'] });
      queryClient.invalidateQueries({ queryKey: ['users', 'me'] });
    },
  });
};

export const useLogout = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => authService.logout(),
    onSettled: () => {
      queryClient.clear();
    },
  });
};

export const useSetup = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: SetupRequest) => authService.setup(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['auth'] });
      queryClient.invalidateQueries({ queryKey: ['users', 'me'] });
    },
  });
};
