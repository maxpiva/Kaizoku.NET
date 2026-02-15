import { useQuery, useMutation, useQueryClient, type UseQueryOptions } from '@tanstack/react-query';
import { providerService } from '@/lib/api/services/providerService';
import { type Provider, type ProviderPreferences } from '@/lib/api/types';

/**
 * Hook to get all available providers (installed and available to install)
 * @param options - Additional query options
 */
export const useProviders = (
  options?: Partial<UseQueryOptions<Provider[], Error>>
) => {
  return useQuery({
    queryKey: ['providers'],
    queryFn: () => providerService.getProviders(),
    staleTime: 2 * 60 * 1000, // 2 minutes
    refetchInterval: 5 * 60 * 1000, // Refetch every 5 minutes
    ...options,
  });
};

/**
 * Hook to install a provider by package name
 * @returns Mutation for installing a provider
 */
export const useInstallProvider = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (pkgName: string) => providerService.installProvider(pkgName),
    onSuccess: () => {
      // Invalidate and refetch providers to get updated list
      queryClient.invalidateQueries({ queryKey: ['providers'] });
    },
  });
};

/**
 * Hook to install a provider from an uploaded file
 * @returns Mutation for installing a provider from file
 */
export const useInstallProviderFromFile = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (file: File) => providerService.installProviderFromFile(file),
    onSuccess: () => {
      // Invalidate and refetch providers to get updated list
      queryClient.invalidateQueries({ queryKey: ['providers'] });
    },
  });
};

/**
 * Hook to uninstall a provider by package name
 * @returns Mutation for uninstalling a provider
 */
export const useUninstallProvider = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (pkgName: string) => providerService.uninstallProvider(pkgName),
    onSuccess: () => {
      // Invalidate and refetch providers to get updated list
      queryClient.invalidateQueries({ queryKey: ['providers'] });
    },
  });
};

/**
 * Hook to get provider preferences by package name
 * @param pkgName - The package name of the provider
 * @param options - Additional query options
 */
export const useProviderPreferences = (
  pkgName: string,
  options?: Partial<UseQueryOptions<ProviderPreferences, Error>>
) => {
  return useQuery({
    queryKey: ['provider-preferences', pkgName],
    queryFn: () => providerService.getProviderPreferences(pkgName),
    enabled: !!pkgName,
    staleTime: 5 * 60 * 1000, // 5 minutes
    ...options,
  });
};

/**
 * Hook to set provider preferences
 * @returns Mutation for setting provider preferences
 */
export const useSetProviderPreferences = () => {
  const queryClient = useQueryClient();
  
  return useMutation({
    mutationFn: (preferences: ProviderPreferences) => 
      providerService.setProviderPreferences(preferences),
    onSuccess: (_, preferences) => {
      // Invalidate the specific provider preferences
      queryClient.invalidateQueries({ 
        queryKey: ['provider-preferences', preferences.pkgName] 
      });
    },
  });
};
