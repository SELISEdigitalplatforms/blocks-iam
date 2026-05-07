import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import {
  mockUserServiceFactory,
  mockGetSessionsPayload,
  mockGetHistoriesPayload,
} from "../../test-utils/__mocks__";
import { userService } from "@blocks-idp/iam/services/user.service";
import { useGetSessions, useGetHistories } from "./use-activity";

vi.mock("@blocks-idp/iam/services/user.service", () => mockUserServiceFactory());

describe("use-activity hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  describe("useGetSessions", () => {
    it("should fetch sessions successfully", async () => {
      const mockResponse = { data: [], totalCount: 0, errors: null };
      vi.mocked(userService.getSessions).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGetSessions(mockGetSessionsPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(userService.getSessions).toHaveBeenCalledWith(mockGetSessionsPayload);
    });
  });

  describe("useGetHistories", () => {
    it("should fetch histories successfully", async () => {
      const mockResponse = { data: [], totalCount: 0, errors: null };
      vi.mocked(userService.getHistories).mockResolvedValue(mockResponse as never);

      const { result } = renderHook(() => useGetHistories(mockGetHistoriesPayload), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(userService.getHistories).toHaveBeenCalledWith(mockGetHistoriesPayload);
    });
  });
});
