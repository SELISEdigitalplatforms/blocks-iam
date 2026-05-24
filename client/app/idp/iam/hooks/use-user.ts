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
    queryKey: ["user"],
    queryFn: async () => {
      const user = await userService.getUser();
      authStore.setUser(user.data);
      return user;
    },
    ...options,
  });
};

export const useGetMe = (options?: { enabled?: boolean }) => {
  const authStore = useAuthStore();
  return useQuery({
    queryKey: ["user"],
    queryFn: async () => {
      try {
        const user = await userService.me();
        if (user.data) {
          authStore.setUser(user.data);
          return user;
        }
        // When impersonated the server looks up the admin user in the impersonated
        // tenant where they don't exist — preserve the last valid user instead.
        if (authStore.user) return { data: authStore.user };
        return user;
      } catch (_error) {
        // When impersonated, /me may return 401 — preserve the last valid user.
        if (authStore.user) return { data: authStore.user };
        throw _error;
      }
    },
    initialData: authStore.user ? { data: authStore.user } : undefined,
    ...options,
  });
};

export const useGetUserById = (options: IGetUserByIdPayload, queryOptions?: { enabled?: boolean }) => {
  return useQuery({
    queryKey: ["user", options],
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

export const useUpdateUser = (options: { id: string; projectKey: string; own?: boolean }) => {
  const queryClient = useQueryClient();
  const { own = false, ...rest } = options;
  return useMutation({
    mutationKey: ["users", "update"],
    mutationFn: userService.updateUser,
    onSuccess: () => {
      if (own) return queryClient.invalidateQueries({ queryKey: ["user"] });
      queryClient.invalidateQueries({ queryKey: ["user", rest] });
    },
  });
};

export const useGetSignUpSetting = (
  option: IGetSignUpSettingPayload,
  options?: { enabled?: boolean },
) => {
  return useQuery({
    queryKey: ["sign-up-setting", option],
    queryFn: () => userService.getSignUpSetting(option),
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

export const useAddRolesAndPermissionToUser = (type?: "role" | "permission") => {
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
  const { isLoading, isFetching, data } = useGetUserById(option);
  const { isPending, mutateAsync } = useUpdateUser(option);

  const slugs = useMemo(() => {
    if (!data) return [];
    return data?.roles.map((item) => item.slug);
  }, [data]);

  const addRoles = useCallback(
    (newSlugs: string[]) => {
      const rolesSlug = new Set([...slugs, ...newSlugs]);
      return mutateAsync({
        ...data?.data,
        itemId: option.id,
        projectKey: option.projectKey,
        roles: Array.from(rolesSlug),
      });
    },
    [data?.data, mutateAsync, option.id, option.projectKey, slugs],
  );

  const deleteRoles = useCallback(
    (deletedSlugs: string[]) => {
      const restSlug = data?.roles
        .filter((item) => !deletedSlugs.includes(item.slug))
        .map((item) => item.slug);
      return mutateAsync({
        ...data?.data,
        roles: restSlug,
        projectKey: option.projectKey,
        itemId: option.id,
      });
    },
    [data?.roles, data?.data, mutateAsync, option.projectKey, option.id],
  );

  return {
    isLoading: isLoading || isFetching,
    isPending,
    roles: data?.roles || [],
    slugs,
    addRoles,
    deleteRoles,
  };
};

export const useUserPermissions = (option: { userId: string; projectKey: string }) => {
  const { isLoading, isFetching, data } = useGetUserById({
    id: option.userId,
    projectKey: option.projectKey,
  });
  const { isPending, mutateAsync } = useUpdateUser({
    id: option.userId,
    projectKey: option.projectKey,
  });

  const resources = useMemo(() => {
    if (!data) return [];
    return data?.permissions.map((item) => item.resource);
  }, [data]);

  const addPermissions = useCallback(
    (newResources: string[]) => {
      const totalResources = new Set([...resources, ...newResources]);
      return mutateAsync({
        ...data?.data,
        itemId: option.userId,
        projectKey: option.projectKey,
        permissions: Array.from(totalResources),
      });
    },
    [mutateAsync, option.userId, resources, option.projectKey],
  );

  const deletePermissions = useCallback(
    (deletedResources: string[]) => {
      const restResources = resources.filter((item) => !deletedResources.includes(item));
      return mutateAsync({
        ...data?.data,
        itemId: option.userId,
        projectKey: option.projectKey,
        permissions: restResources,
      });
    },
    [mutateAsync, option.userId, resources, option.projectKey],
  );

  return {
    isLoading: isLoading || isFetching,
    isPending,
    permissions: data?.permissions || [],
    resources,
    addPermissions,
    deletePermissions,
  };
};
