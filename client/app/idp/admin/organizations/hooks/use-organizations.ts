import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { organizationService } from '../services/organization.service';
import {
  Organization,
  CreateOrganizationRequest,
  UpdateOrganizationRequest,
  OrganizationConfig,
  SaveOrganizationConfigRequest,
} from '@blocks-idp/shared/models/admin.models';
import { showErrorToast, showSuccessToast } from '@/hooks/use-toast';

/**
 * Query Keys for organization management
 */
const organizationQueryKeys = {
  all: ['organizations'] as const,
  list: (page?: number) => [...organizationQueryKeys.all, 'list', page] as const,
  detail: (orgId: string) => [...organizationQueryKeys.all, 'detail', orgId] as const,
  config: (orgId: string) => [...organizationQueryKeys.all, 'config', orgId] as const,
};

/**
 * Get list of organizations
 */
export const useGetOrganizations = (page?: number, pageSize?: number) => {
  return useQuery({
    queryKey: organizationQueryKeys.list(page),
    queryFn: () => organizationService.getOrganizations(page, pageSize),
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

/**
 * Get single organization details
 */
export const useGetOrganization = (organizationId: string) => {
  return useQuery({
    queryKey: organizationQueryKeys.detail(organizationId),
    queryFn: () => organizationService.getOrganization(organizationId),
    enabled: !!organizationId,
  });
};

/**
 * Get organization configuration
 */
export const useGetOrganizationConfig = (organizationId: string) => {
  return useQuery({
    queryKey: organizationQueryKeys.config(organizationId),
    queryFn: () => organizationService.getOrganizationConfig(organizationId),
    enabled: !!organizationId,
  });
};

/**
 * Create new organization
 */
export const useCreateOrganization = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateOrganizationRequest) => organizationService.createOrganization(data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: organizationQueryKeys.all });
      showSuccessToast({ description: 'Organization created successfully' });
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to create organization';
      showErrorToast({ errors: errorMsg });
    },
  });
};

/**
 * Update organization
 */
export const useUpdateOrganization = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: UpdateOrganizationRequest) => organizationService.updateOrganization(data),
    onSuccess: (response, variables) => {
      queryClient.invalidateQueries({ queryKey: organizationQueryKeys.detail(variables.id) });
      queryClient.invalidateQueries({ queryKey: organizationQueryKeys.all });
      showSuccessToast({ description: 'Organization updated successfully' });
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to update organization';
      showErrorToast({ errors: errorMsg });
    },
  });
};

/**
 * Save organization configuration
 */
export const useSaveOrganizationConfig = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: SaveOrganizationConfigRequest) => organizationService.saveOrganizationConfig(data),
    onSuccess: (response, variables) => {
      queryClient.invalidateQueries({ queryKey: organizationQueryKeys.config(variables.organization_id) });
      showSuccessToast({ description: 'Configuration saved successfully' });
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to save configuration';
      showErrorToast({ errors: errorMsg });
    },
  });
};
