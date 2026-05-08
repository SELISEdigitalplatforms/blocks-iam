import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { userManagementService } from '../services/user-management.service';
import {
  User,
  CreateUserRequest,
  UpdateUserRequest,
  GetUsersRequest,
  GetUsersResponse,
  GetUserResponse,
} from '@blocks-idp/shared/models/admin.models';
import { showErrorToast, showSuccessToast } from '@/hooks/use-toast';

/**
 * Query Keys for user management
 */
const userQueryKeys = {
  all: ['users'] as const,
  list: (query: GetUsersRequest) => [...userQueryKeys.all, 'list', query] as const,
  detail: (userId: string) => [...userQueryKeys.all, 'detail', userId] as const,
  checkEmail: (email: string) => [...userQueryKeys.all, 'checkEmail', email] as const,
  timelines: (userId: string) => [...userQueryKeys.all, 'timelines', userId] as const,
};

/**
 * Get paginated list of users
 */
export const useGetUsers = (query: GetUsersRequest) => {
  return useQuery({
    queryKey: userQueryKeys.list(query),
    queryFn: () => userManagementService.getUsers(query),
    staleTime: 1000 * 60 * 5, // 5 minutes
  });
};

/**
 * Get single user details
 */
export const useGetUser = (userId: string) => {
  return useQuery({
    queryKey: userQueryKeys.detail(userId),
    queryFn: () => userManagementService.getUser(userId),
    enabled: !!userId,
  });
};

/**
 * Check if email is available
 */
export const useCheckEmailAvailability = (email: string, debounceMs = 500) => {
  return useQuery({
    queryKey: userQueryKeys.checkEmail(email),
    queryFn: () => userManagementService.checkEmailAvailability(email),
    enabled: !!email && email.length > 3,
    staleTime: 1000 * 60 * 5,
  });
};

/**
 * Get user activity timeline
 */
export const useGetUserTimelines = (userId: string) => {
  return useQuery({
    queryKey: userQueryKeys.timelines(userId),
    queryFn: () => userManagementService.getUserTimelines(userId),
    enabled: !!userId,
  });
};

/**
 * Create new user
 */
export const useCreateUser = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: CreateUserRequest) => userManagementService.createUser(data),
    onSuccess: (response) => {
      // Invalidate users list to refetch
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });

      showSuccessToast({
        description: 'User created successfully',
      });

      return response;
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to create user';
      showErrorToast({ errors: errorMsg });
    },
  });
};

/**
 * Update user information
 */
export const useUpdateUser = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (data: UpdateUserRequest) => userManagementService.updateUser(data),
    onSuccess: (response, variables) => {
      // Invalidate specific user detail and list
      queryClient.invalidateQueries({ queryKey: userQueryKeys.detail(variables.id) });
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });

      showSuccessToast({
        description: 'User updated successfully',
      });

      return response;
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to update user';
      showErrorToast({ errors: errorMsg });
    },
  });
};

/**
 * Deactivate user account
 */
export const useDeactivateUser = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (userId: string) => userManagementService.deactivateUser(userId),
    onSuccess: (response, userId) => {
      // Invalidate specific user and list
      queryClient.invalidateQueries({ queryKey: userQueryKeys.detail(userId) });
      queryClient.invalidateQueries({ queryKey: userQueryKeys.all });

      showSuccessToast({
        description: 'User deactivated successfully',
      });

      return response;
    },
    onError: (error: any) => {
      const errorMsg = error?.error?.errors?.error_description || 'Failed to deactivate user';
      showErrorToast({ errors: errorMsg });
    },
  });
};
