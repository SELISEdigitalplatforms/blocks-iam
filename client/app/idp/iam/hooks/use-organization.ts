import { keepPreviousData, useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { IGetOrganizationByIdParams, IOrganizationFilter } from "@blocks-idp/iam/models/organization";
import { IOrganizationConfigPayload } from "@blocks-idp/iam/models/organization-config.model";
import { IUpdateOrganizationPayload } from "@blocks-idp/iam/models/organization";
import { iamService } from "@blocks-idp/iam/services/iam.service";

export const useGetOrganizations = (options: IOrganizationFilter) => {
  return useQuery({
    queryKey: ["organizations", options],
    queryFn: () =>
      iamService.organization.getOrganizations({
        page: options.page,
        pageSize: options.pageSize,
        search: options.search,
        sort: options.sort,
      }),
    placeholderData: keepPreviousData,
  });
};

export const useGetOrganizationById = (params: IGetOrganizationByIdParams) => {
  return useQuery({
    queryKey: ["organization", params.itemId, params.projectKey],
    queryFn: () => iamService.organization.getOrganizationById(params),
    enabled: !!params.itemId && !!params.projectKey,
  });
};

export const useSaveOrganization = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["organization", "createOrUpdate"],
    mutationFn: iamService.organization.saveOrganization,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["organizations"] });
      queryClient.invalidateQueries({ queryKey: ["organization"] });
      queryClient.invalidateQueries({ queryKey: ["organizations", "my"] });
    },
  });
};

export const useUpdateOrganization = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["organization", "update"],
    mutationFn: (payload: IUpdateOrganizationPayload) =>
      iamService.organization.updateOrganization(payload),
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({ queryKey: ["organizations"] });
      queryClient.invalidateQueries({ queryKey: ["organization"] });
      queryClient.invalidateQueries({ queryKey: ["organizations", "my"] });
      if (variables?.itemId) {
        queryClient.invalidateQueries({
          queryKey: ["organization", variables.itemId],
        });
      }
    },
  });
};

export const useGetOrganizationConfig = (projectId?: string) => {
  return useQuery({
    queryKey: ["organization", "config", projectId],
    queryFn: () => iamService.organization.getOrganizationConfig(),
    enabled: !!projectId,
  });
};

// Signup runs anonymously and must scope the read to the tenant from the OIDC
// request, so it always fires — unlike the admin hook above, which gates on a
// selected project.
export const useGetSignupOrganizationConfig = (
  tenantId?: string,
  options?: { enabled?: boolean },
) => {
  return useQuery({
    queryKey: ["organization", "config", "signup", tenantId],
    queryFn: () => iamService.organization.getOrganizationConfig(tenantId),
    ...options,
  });
};

export const useGetMyOrganizations = () => {
  return useQuery({
    queryKey: ["organizations", "my"],
    queryFn: () => iamService.organization.getMyOrganizations(),
  });
};

export const useSaveOrganizationConfig = (projectId: string) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["organization", "config", "save"],
    mutationFn: (payload: IOrganizationConfigPayload) =>
      iamService.organization.saveOrganizationConfig(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["organization", "config", projectId] });
    },
  });
};
