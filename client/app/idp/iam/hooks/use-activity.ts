import { IGetHistoriesPayload, IGetSessionPayload } from "@blocks-idp/iam/models/user";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useQuery } from "@tanstack/react-query";

export const useGetSessions = (option: IGetSessionPayload) => {
  return useQuery({
    queryKey: ["sessions", option],
    queryFn: () => userService.getSessions(option),
  });
};

export const useGetHistories = (option: IGetHistoriesPayload) => {
  return useQuery({
    queryKey: ["histories", option],
    queryFn: () => userService.getHistories(option),
  });
};
