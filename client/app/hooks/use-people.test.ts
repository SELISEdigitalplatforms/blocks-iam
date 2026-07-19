import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockProjectStoreFactory } from "@/test-utils/__mocks__";
import { useProjectStore } from "@/store/useProjectStore";
import { peopleService } from "@blocks-identifier/services/people.service";
import {
  useGetPeople,
  useInvitePeople,
  useResendInvitation,
  useRemoveAccess,
  useRemoveEnvironmentAccess,
  useConfirmInvitation,
  useTransferOwnership,
} from "./use-people";

vi.mock("@blocks-identifier/services/people.service", () => ({
  peopleService: {
    getPeople: vi.fn(),
    invitePeople: vi.fn(),
    resendInvitation: vi.fn(),
    removeAccess: vi.fn(),
    removeEnvironmentAccess: vi.fn(),
    confirmInvitation: vi.fn(),
    transferOwnership: vi.fn(),
    peopleAcceptInvitation: vi.fn(),
  },
}));

vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

const TEST_GROUP_ID = "test-tenant-group-id";

const mockPeopleResponse = {
  peoples: [{ userId: "user-1", email: "a@b.com" }],
  totalCount: 1,
  isOwner: true,
  errors: null,
  isSuccess: true,
};

const okMutationResponse = { isSuccess: true, errors: null };

describe("People Hooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Restore the default store shape (individual tests may override it).
    vi.mocked(useProjectStore).mockReturnValue({
      selectedTenantGroup: TEST_GROUP_ID,
    } as unknown as ReturnType<typeof useProjectStore>);
  });

  // ─── useGetPeople ──────────────────────────────────────────────────────────
  describe("useGetPeople", () => {
    it("should fetch people scoped to the tenant group and transform the response", async () => {
      vi.mocked(peopleService.getPeople).mockResolvedValue(mockPeopleResponse);

      const option = { page: 0, pageSize: 10, filter: "" };
      const { result } = renderHook(() => useGetPeople(option), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual({
        peoples: mockPeopleResponse.peoples,
        totalCount: 1,
        isOwner: true,
      });
      expect(peopleService.getPeople).toHaveBeenCalledWith({
        ...option,
        projectGroupId: TEST_GROUP_ID,
      });
    });

    it("should be disabled when no tenant group is selected", async () => {
      vi.mocked(useProjectStore).mockReturnValue({
        selectedTenantGroup: "",
      } as unknown as ReturnType<typeof useProjectStore>);
      vi.mocked(peopleService.getPeople).mockResolvedValue(mockPeopleResponse);

      const { result } = renderHook(
        () => useGetPeople({ page: 0, pageSize: 10, filter: "" }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
      expect(peopleService.getPeople).not.toHaveBeenCalled();
    });

    it("should surface errors", async () => {
      vi.mocked(peopleService.getPeople).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(
        () => useGetPeople({ page: 0, pageSize: 10, filter: "" }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useInvitePeople ───────────────────────────────────────────────────────
  describe("useInvitePeople", () => {
    it("should invite people", async () => {
      vi.mocked(peopleService.invitePeople).mockResolvedValue(okMutationResponse as never);

      const payload = { emails: ["a@b.com"], roles: ["reader"] };
      const { result } = renderHook(() => useInvitePeople(), { wrapper: createWrapper() });

      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(peopleService.invitePeople).toHaveBeenCalledWith(payload, expect.anything());
    });

    it("should surface errors", async () => {
      vi.mocked(peopleService.invitePeople).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useInvitePeople(), { wrapper: createWrapper() });

      result.current.mutate({ emails: [] } as never);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useResendInvitation ───────────────────────────────────────────────────
  describe("useResendInvitation", () => {
    it("should resend an invitation", async () => {
      vi.mocked(peopleService.resendInvitation).mockResolvedValue(okMutationResponse);

      const payload = { email: "a@b.com" };
      const { result } = renderHook(() => useResendInvitation(), { wrapper: createWrapper() });

      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(peopleService.resendInvitation).toHaveBeenCalledWith(payload, expect.anything());
    });
  });

  // ─── useRemoveAccess ───────────────────────────────────────────────────────
  describe("useRemoveAccess", () => {
    it("should remove access", async () => {
      vi.mocked(peopleService.removeAccess).mockResolvedValue(okMutationResponse);

      const payload = { userId: "user-1" };
      const { result } = renderHook(() => useRemoveAccess(), { wrapper: createWrapper() });

      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(peopleService.removeAccess).toHaveBeenCalledWith(payload, expect.anything());
    });
  });

  // ─── useRemoveEnvironmentAccess ────────────────────────────────────────────
  describe("useRemoveEnvironmentAccess", () => {
    it("should remove environment access", async () => {
      vi.mocked(peopleService.removeEnvironmentAccess).mockResolvedValue(okMutationResponse);

      const payload = { userId: "user-1", environment: "dev" };
      const { result } = renderHook(() => useRemoveEnvironmentAccess(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(peopleService.removeEnvironmentAccess).toHaveBeenCalledWith(payload, expect.anything());
    });
  });

  // ─── useConfirmInvitation ──────────────────────────────────────────────────
  describe("useConfirmInvitation", () => {
    it("should confirm an invitation", async () => {
      vi.mocked(peopleService.confirmInvitation).mockResolvedValue({
        isSuccess: true,
        errors: null,
        activationKey: "key-1",
      });

      const payload = { invitationId: "inv-1" };
      const { result } = renderHook(() => useConfirmInvitation(), { wrapper: createWrapper() });

      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(peopleService.confirmInvitation).toHaveBeenCalledWith(payload, expect.anything());
    });
  });

  // ─── useTransferOwnership ──────────────────────────────────────────────────
  describe("useTransferOwnership", () => {
    it("should transfer ownership", async () => {
      vi.mocked(peopleService.transferOwnership).mockResolvedValue(okMutationResponse);

      const payload = { newOwnerId: "user-2" };
      const { result } = renderHook(() => useTransferOwnership(), { wrapper: createWrapper() });

      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(peopleService.transferOwnership).toHaveBeenCalledWith(payload, expect.anything());
    });

    it("should surface errors", async () => {
      vi.mocked(peopleService.transferOwnership).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useTransferOwnership(), { wrapper: createWrapper() });

      result.current.mutate({ newOwnerId: "user-2" } as never);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });
});
