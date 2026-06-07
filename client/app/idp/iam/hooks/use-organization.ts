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
        searchText: options.search,
      }),
    enabled: !!options.projectKey,
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
    },
  });
};

export const useUpdateOrganization = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["organization", "update"],
    mutationFn: (payload: IUpdateOrganizationPayload) =>
      iamService.organization.updateOrganization(payload),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["organizations"] });
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
