import { useAuthStore } from "@/store/useAuthStore";
import {
  IGetUserByIdPayload,
  IGetUserRolesPayload,
  IGetUsersPayload,
  IGetSignUpSettingPayload,
} from "@blocks-idp/iam/models/user";
import { userService } from "@blocks-idp/iam/services/user.service";
import { normalizeSearchQueryText } from "@/lib/utils";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useMemo } from "react";

export const useGetUsers = (option: IGetUsersPayload) => {
  const { page, pageSize, projectKey, filter, sort } = option;

  const payload = useMemo(() => {
    if (!filter) {
      return { page, pageSize, sort };
    }
    return {
      page,
      pageSize,
      sort,
      filter: {
        ...filter,
        email: normalizeSearchQueryText(filter.email ?? ""),
        name: normalizeSearchQueryText(filter.name ?? ""),
      },
    };
  }, [
    page,
    pageSize,
    projectKey,
    filter?.email,
    filter?.name,
    filter?.organizationId,
    sort?.property,
    sort?.isDescending,
  ]);

  return useQuery({
    queryKey: ["users", projectKey, payload],
    queryFn: () => userService.getUsers(payload),
    enabled: !!projectKey,
  });
};

export const useGetUser = (options?: { enabled?: boolean }) => {
  const authStore = useAuthStore();
  return useQuery({
    queryKey: ["userAPiNotinuse"],
    queryFn: async () => {
      const user = await userService.getUser();
      authStore.setUser(user.data);
      return user;
    },
    staleTime: Infinity,
    ...options,
  });
};

export const useGetMe = (options?: { enabled?: boolean }) => {
  const authStore = useAuthStore();
  return useQuery({
    queryKey: ["user"],
    queryFn: async () => {
      const user = await userService.me();
      if (user.data) authStore.setUser(user.data);
      return user;
    },
    initialData: authStore.user ? { data: authStore.user } : undefined,
    staleTime: Infinity,
    ...options,
  });
};

export const useGetUserById = (
  options: IGetUserByIdPayload,
  queryOptions?: { enabled?: boolean },
) => {
  return useQuery({
    queryKey: ["user-by-id", options],
    queryFn: () => userService.getUserById(options),
    ...queryOptions,
  });
};

export const useAddUser = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["users", "add"],
    mutationFn: userService.addUser,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["users"] });
      queryClient.invalidateQueries({ queryKey: ["subscription-usage"] });
    },
  });
};

export const useUpdateUser = (options: {
  id: string;
  projectKey: string;
  own?: boolean;
}) => {
  const queryClient = useQueryClient();
  const { own = false, ...rest } = options;
  return useMutation({
    mutationKey: ["users", "update"],
    mutationFn: userService.updateUser,
    onSuccess: () => {
      if (own) return queryClient.invalidateQueries({ queryKey: ["user"] });
      queryClient.invalidateQueries({ queryKey: ["user-by-id", rest] });
    },
  });
};

export const useGetSignUpSetting = (
  option: IGetSignUpSettingPayload,
  options?: { enabled?: boolean },
) => {
  return useQuery({
    queryKey: ["sign-up-setting", option],
    queryFn: () => userService.getSignUpSetting(),
    ...options,
  });
};

export const useSaveSignUpSetting = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["sign-up-setting", "save"],
    mutationFn: userService.saveSignUpSetting,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["sign-up-setting"] });
    },
  });
};

export const useAddRolesAndPermissionToUser = (
  type?: "role" | "permission",
) => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["users", "add roles and permissions"],
    mutationFn: userService.saveRolesAndPermissions,
    onSuccess: () => {
      if (type === "role") {
        queryClient.invalidateQueries({ queryKey: ["user-roles"] });
      }
      queryClient.invalidateQueries({ queryKey: ["user-permissions"] });
    },
  });
};

export const useGetUserRoles = (option: IGetUserRolesPayload) => {
  return useQuery({
    queryKey: ["user-roles", option],
    queryFn: () => userService.getUserRoles(option),
  });
};

export const useGetUserPermissions = (option: IGetUserRolesPayload) => {
  return useQuery({
    queryKey: ["user-permissions", option],
    queryFn: () => userService.getUserPermissions(option),
  });
};

export const useUserRoles = (option: { id: string; projectKey: string }) => {
  const { isLoading: isUserLoading, isFetching, data: userData } = useGetUserById(option);
  const { isLoading: isRolesLoading, data: rolesData } = useGetUserRoles({
    userId: option.id,
  });
  const { isPending, mutateAsync } = useUpdateUser(option);

  const slugs = useMemo(() => {
    if (!rolesData?.data) return [];
    return rolesData.data.map((item) => item.slug);
  }, [rolesData]);

  const addRoles = useCallback(
    (newSlugs: string[]) => {
      const rolesSlug = new Set([...slugs, ...newSlugs]);
      return mutateAsync({
        ...userData?.data,
        itemId: option.id,
        roles: Array.from(rolesSlug),
      });
    },
    [userData?.data, mutateAsync, option.id, option.projectKey, slugs],
  );

  const deleteRoles = useCallback(
    (deletedSlugs: string[]) => {
      const restSlug = slugs.filter((slug) => !deletedSlugs.includes(slug));
      return mutateAsync({
        ...userData?.data,
        roles: restSlug,
        itemId: option.id,
      });
    },
    [slugs, userData?.data, mutateAsync, option.projectKey, option.id],
  );

  return {
    isLoading: isUserLoading || isFetching || isRolesLoading,
    isPending,
    roles: rolesData?.data || [],
    slugs,
    addRoles,
    deleteRoles,
  };
};

export const useUserPermissions = (option: {
  userId: string;
  projectKey: string;
}) => {
  const { isLoading: isUserLoading, isFetching, data: userData } = useGetUserById({
    id: option.userId,
    projectKey: option.projectKey,
  });
  const { isLoading: isPermissionsLoading, data: permissionsData } = useGetUserPermissions({
    userId: option.userId,
  });
  const { isPending, mutateAsync } = useUpdateUser({
    id: option.userId,
    projectKey: option.projectKey,
  });

  const resources = useMemo(() => {
    if (!permissionsData?.data) return [];
    return permissionsData.data.map((item) => item.resource);
  }, [permissionsData]);

  const addPermissions = useCallback(
    (newResources: string[]) => {
      const totalResources = new Set([...resources, ...newResources]);
      return mutateAsync({
        ...userData?.data,
        itemId: option.userId,
        permissions: Array.from(totalResources),
      });
    },
    [mutateAsync, option.userId, resources, option.projectKey, userData?.data],
  );

  const deletePermissions = useCallback(
    (deletedResources: string[]) => {
      const restResources = resources.filter(
        (item) => !deletedResources.includes(item),
      );
      return mutateAsync({
        ...userData?.data,
        itemId: option.userId,
        permissions: restResources,
      });
    },
    [mutateAsync, option.userId, resources, option.projectKey, userData?.data],
  );

  return {
    isLoading: isUserLoading || isFetching || isPermissionsLoading,
    isPending,
    permissions: permissionsData?.data || [],
    resources,
    addPermissions,
    deletePermissions,
  };
};
