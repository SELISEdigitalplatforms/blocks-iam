import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { notificationService } from "../services/notification.service";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { getRuntimeEnv } from "@/lib/runtime-env";

export const useGetNotifications = (pageNumber: number, pageSize: number) => {
  return useQuery({
    queryKey: ["notifications", pageNumber, pageSize],
    queryFn: () => notificationService.getNotifications(pageNumber, pageSize),
    staleTime: 0,
  });
};

export const useMarkAsRead = () => {
  return useMutation({
    mutationKey: ["markAsRead"],
    mutationFn: notificationService.markAsRead,
  });
};

export const useMarkAllAsRead = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["markAllAsRead"],
    mutationFn: notificationService.markAllNotificationsAsRead,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["notifications"] });
    },
  });
};

export const useGetBlocksNotificationConfig = (page: number = 0, pageSize: number = 100) => {
  return useQuery({
    queryKey: ["blocksNotificationConfigs", page, pageSize],
    queryFn: () =>
      notificationService.getNotificationConfigs(page, pageSize, getRuntimeEnv("BLOCKS_X_BLOCKS_KEY")),
  });
};

export const useGetNotificationConfigs = (page: number = 0, pageSize: number = 10) => {
  const tenantId = useProjectStore()?.selectedProject?.tenantId || "";
  return useQuery({
    queryKey: ["notificationConfigs", page, pageSize, tenantId],
    queryFn: () => notificationService.getNotificationConfigs(page, pageSize, tenantId),
    enabled: !!tenantId,
  });
};

export const useSaveNotificationConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["saveNotificationConfig"],
    mutationFn: notificationService.saveNotificationConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["notificationConfigs"] });
    },
  });
};

export const useDeleteNotificationConfig = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationKey: ["deleteNotificationConfig"],
    mutationFn: notificationService.deleteNotificationConfig,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["notificationConfigs"] });
    },
  });
};
