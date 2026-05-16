import { useQuery } from '@tanstack/react-query';
import { sessionService } from '../services/session.service';
import { GetActivityRequest } from '@blocks-idp/shared/models/admin.models';

/**
 * Query Keys for session & activity management
 */
const sessionQueryKeys = {
  all: ['sessions'] as const,
  list: () => [...sessionQueryKeys.all, 'list'] as const,
  history: (request: GetActivityRequest) => [...sessionQueryKeys.all, 'history', request] as const,
};

/**
 * Get user's active sessions
 */
export const useGetSessions = () => {
  return useQuery({
    queryKey: sessionQueryKeys.list(),
    queryFn: () => sessionService.getSessions(),
    staleTime: 1000 * 60, // 1 minute
  });
};

/**
 * Get activity history
 */
export const useGetActivityHistory = (request: GetActivityRequest) => {
  return useQuery({
    queryKey: sessionQueryKeys.history(request),
    queryFn: () => sessionService.getActivityHistory(request),
    staleTime: 1000 * 60, // 1 minute
  });
};
