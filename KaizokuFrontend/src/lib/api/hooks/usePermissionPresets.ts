import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { permissionPresetService } from '@/lib/api/services/permissionPresetService';
import type { CreatePresetRequest, UpdatePresetRequest } from '@/lib/api/auth-types';

export const usePermissionPresets = () => {
  return useQuery({
    queryKey: ['permission-presets'],
    queryFn: () => permissionPresetService.listPresets(),
    staleTime: 60_000,
  });
};

export const useCreatePreset = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreatePresetRequest) => permissionPresetService.createPreset(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['permission-presets'] });
    },
  });
};

export const useUpdatePreset = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdatePresetRequest }) =>
      permissionPresetService.updatePreset(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['permission-presets'] });
    },
  });
};

export const useDeletePreset = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => permissionPresetService.deletePreset(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['permission-presets'] });
    },
  });
};

export const useSetDefaultPreset = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => permissionPresetService.setDefaultPreset(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['permission-presets'] });
    },
  });
};

export const useClearDefaultPreset = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => permissionPresetService.clearDefaultPreset(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['permission-presets'] });
    },
  });
};
