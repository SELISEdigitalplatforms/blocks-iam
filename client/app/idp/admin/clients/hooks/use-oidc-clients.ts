import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { oidcClientService } from '../services/oidc-client.service';
import { CreateOidcClientRequest } from '@blocks-idp/shared/models/admin.models';
import { showErrorToast, showSuccessToast } from '@/hooks/use-toast';

/**
 * Query Keys for OIDC client management
 */
const oidcClientQueryKeys = {
  all: ['oidcClients'] as const,
  list: () => [...oidcClientQueryKeys.all, 'list'] as const,
  detail: (clientId: string) => [...oidcClientQueryKeys.all, 'detail', clientId] as const,
};

/**
 * Get list of OIDC clients
 */
export const useGetOidcClients = () => {
  return useQuery({
    queryKey: oidcClientQueryKeys.list(),
    queryFn: () => oidcClientService.getClients(),
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

/**
 * Get single OIDC client details
 */
export const useGetOidcClient = (clientId: string) => {
  return useQuery({
    queryKey: oidcClientQueryKeys.detail(clientId),
    queryFn: () => oidcClientService.getClient(clientId),
    enabled: !!clientId,
  });
};

/**
 * Create OIDC client
 */
export const useCreateOidcClient = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateOidcClientRequest) => oidcClientService.createClient(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: oidcClientQueryKeys.all });
      showSuccessToast({ description: 'OIDC client created successfully' });
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to create client';
      showErrorToast({ errors: errorMsg });
    },
  });
};

/**
 * Update OIDC client
 */
export const useUpdateOidcClient = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ clientId, data }: { clientId: string; data: CreateOidcClientRequest }) =>
      oidcClientService.updateClient(clientId, data),
    onSuccess: (response, variables) => {
      queryClient.invalidateQueries({ queryKey: oidcClientQueryKeys.detail(variables.clientId) });
      queryClient.invalidateQueries({ queryKey: oidcClientQueryKeys.all });
      showSuccessToast({ description: 'OIDC client updated successfully' });
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to update client';
      showErrorToast({ errors: errorMsg });
    },
  });
};

/**
 * Delete OIDC client
 */
export const useDeleteOidcClient = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (clientId: string) => oidcClientService.deleteClient(clientId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: oidcClientQueryKeys.all });
      showSuccessToast({ description: 'OIDC client deleted successfully' });
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to delete client';
      showErrorToast({ errors: errorMsg });
    },
  });
};
