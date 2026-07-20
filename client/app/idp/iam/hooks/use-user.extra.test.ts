import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const { userService } = vi.hoisted(() => ({
  userService: {
    getUsers: vi.fn(),
    getUser: vi.fn(),
    me: vi.fn(),
    getUserById: vi.fn(),
    isUserExist: vi.fn(),
    addUser: vi.fn(),
    updateUser: vi.fn(),
    updateMe: vi.fn(),
    getSignUpSetting: vi.fn(),
    saveSignUpSetting: vi.fn(),
    saveRolesAndPermissions: vi.fn(),
    getUserRoles: vi.fn(),
    getUserPermissions: vi.fn(),
    updateUserAccessControl: vi.fn(),
    revokeAccess: vi.fn(),
  },
}));

vi.mock("@blocks-idp/iam/services/user.service", () => ({ userService }));

import {
  useGetUsers,
  useGetMe,
  useGetUserById,
  useCheckUserExists,
  useUpdateUser,
  useAddRolesAndPermissionToUser,
  useUpdateUserAccessControl,
  useRevokeAccess,
  useUserRoles,
  useUserPermissions,
} from "./use-user";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useGetUsers filter normalization", () => {
  it("normalizes email/name search text (short terms become empty)", async () => {
    userService.getUsers.mockResolvedValue({ users: [], totalCount: 0 });
    const { result } = renderHook(
      () =>
        useGetUsers({
          page: 0,
          pageSize: 10,
          projectKey: "pk",
          sort: undefined,
          filter: { email: "  hi ", name: "Alexander" },
        } as never),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.getUsers).toHaveBeenCalledWith(
      expect.objectContaining({
        filter: expect.objectContaining({ email: "", name: "Alexander" }),
      }),
    );
  });

  it("is disabled without a projectKey", () => {
    const { result } = renderHook(
      () => useGetUsers({ page: 0, pageSize: 10, projectKey: "", sort: undefined } as never),
      { wrapper: createWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
    expect(userService.getUsers).not.toHaveBeenCalled();
  });
});

describe("useGetMe.userFound", () => {
  it("is true when me() returns a non-empty user object", async () => {
    userService.me.mockResolvedValue({ data: { itemId: "u1" } });
    const { result } = renderHook(() => useGetMe(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.userFound).toBe(true);
  });

  it("is false when me() returns an empty object", async () => {
    userService.me.mockResolvedValue({ data: {} });
    const { result } = renderHook(() => useGetMe(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.userFound).toBe(false);
  });
});

describe("useCheckUserExists email gating", () => {
  it("does not query for an invalid email", () => {
    const { result } = renderHook(() => useCheckUserExists("not-an-email"), {
      wrapper: createWrapper(),
    });
    expect(result.current.fetchStatus).toBe("idle");
    expect(userService.isUserExist).not.toHaveBeenCalled();
  });

  it("queries with the trimmed email for a valid address", async () => {
    userService.isUserExist.mockResolvedValue({ exists: true });
    const { result } = renderHook(() => useCheckUserExists("  User@Example.com "), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.isUserExist).toHaveBeenCalledWith("User@Example.com");
  });
});

describe("useGetUserById gating", () => {
  it("respects the explicit enabled option", () => {
    const { result } = renderHook(
      () => useGetUserById({ id: "u1", projectKey: "pk" }, { enabled: false }),
      { wrapper: createWrapper() },
    );
    expect(result.current.fetchStatus).toBe("idle");
    expect(userService.getUserById).not.toHaveBeenCalled();
  });
});

describe("useUpdateUser own vs other", () => {
  it("calls updateMe when own is true", async () => {
    userService.updateMe.mockResolvedValue(undefined);
    const { result } = renderHook(
      () => useUpdateUser({ id: "u1", projectKey: "pk", own: true }),
      { wrapper: createWrapper() },
    );
    result.current.mutate({ itemId: "u1" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.updateMe).toHaveBeenCalled();
    expect(userService.updateUser).not.toHaveBeenCalled();
  });

  it("calls updateUser when own is false", async () => {
    userService.updateUser.mockResolvedValue(undefined);
    const { result } = renderHook(
      () => useUpdateUser({ id: "u1", projectKey: "pk" }),
      { wrapper: createWrapper() },
    );
    result.current.mutate({ itemId: "u1" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.updateUser).toHaveBeenCalled();
  });
});

describe("useAddRolesAndPermissionToUser", () => {
  it("mutates for the role type", async () => {
    userService.saveRolesAndPermissions.mockResolvedValue(undefined);
    const { result } = renderHook(() => useAddRolesAndPermissionToUser("role"), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ userId: "u1" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.saveRolesAndPermissions).toHaveBeenCalled();
  });
});

describe("useUpdateUserAccessControl / useRevokeAccess", () => {
  it("injects the userId into the access-control payload", async () => {
    userService.updateUserAccessControl.mockResolvedValue(undefined);
    const { result } = renderHook(
      () => useUpdateUserAccessControl({ id: "u9", projectKey: "pk" }),
      { wrapper: createWrapper() },
    );
    result.current.mutate({ roles: ["admin"] } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.updateUserAccessControl).toHaveBeenCalledWith(
      expect.objectContaining({ userId: "u9", roles: ["admin"] }),
    );
  });

  it("injects the userId into the revoke payload", async () => {
    userService.revokeAccess.mockResolvedValue(undefined);
    const { result } = renderHook(() => useRevokeAccess({ id: "u9" }), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ reason: "offboarding" } as never);
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(userService.revokeAccess).toHaveBeenCalledWith(
      expect.objectContaining({ userId: "u9", reason: "offboarding" }),
    );
  });
});

describe("useUserRoles composite", () => {
  it("derives slugs and merges new roles on addRoles", async () => {
    userService.getUserById.mockResolvedValue({
      data: { organizationIds: ["org1"], permissions: { res: ["read"] } },
    });
    userService.getUserRoles.mockResolvedValue({ data: [{ slug: "viewer" }] });
    userService.updateUser.mockResolvedValue(undefined);

    const { result } = renderHook(() => useUserRoles({ id: "u1", projectKey: "pk" }), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.slugs).toEqual(["viewer"]));
    expect(result.current.roles).toEqual([{ slug: "viewer" }]);

    await result.current.addRoles(["editor"]);
    expect(userService.updateUser).toHaveBeenCalledWith(
      expect.objectContaining({
        itemId: "u1",
        organizations: ["org1"],
        roles: expect.arrayContaining(["viewer", "editor"]),
        permissions: ["read"],
      }),
    );
  });

  it("removes roles on deleteRoles", async () => {
    userService.getUserById.mockResolvedValue({ data: { organizationIds: [] } });
    userService.getUserRoles.mockResolvedValue({ data: [{ slug: "a" }, { slug: "b" }] });
    userService.updateUser.mockResolvedValue(undefined);

    const { result } = renderHook(() => useUserRoles({ id: "u1", projectKey: "pk" }), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.slugs).toEqual(["a", "b"]));

    await result.current.deleteRoles(["a"]);
    expect(userService.updateUser).toHaveBeenCalledWith(
      expect.objectContaining({ roles: ["b"] }),
    );
  });
});

describe("useUserPermissions composite", () => {
  it("derives resources and merges new permissions on addPermissions", async () => {
    userService.getUserById.mockResolvedValue({
      data: { organizationIds: ["org1"], roles: { r: ["admin"] } },
    });
    userService.getUserPermissions.mockResolvedValue({ data: [{ resource: "users" }] });
    userService.updateUser.mockResolvedValue(undefined);

    const { result } = renderHook(
      () => useUserPermissions({ userId: "u1", projectKey: "pk" }),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.resources).toEqual(["users"]));

    await result.current.addPermissions(["roles"]);
    expect(userService.updateUser).toHaveBeenCalledWith(
      expect.objectContaining({
        itemId: "u1",
        organizations: ["org1"],
        roles: ["admin"],
        permissions: expect.arrayContaining(["users", "roles"]),
      }),
    );
  });

  it("removes permissions on deletePermissions", async () => {
    userService.getUserById.mockResolvedValue({ data: {} });
    userService.getUserPermissions.mockResolvedValue({
      data: [{ resource: "users" }, { resource: "roles" }],
    });
    userService.updateUser.mockResolvedValue(undefined);

    const { result } = renderHook(
      () => useUserPermissions({ userId: "u1", projectKey: "pk" }),
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(result.current.resources).toEqual(["users", "roles"]));

    await result.current.deletePermissions(["users"]);
    expect(userService.updateUser).toHaveBeenCalledWith(
      expect.objectContaining({ permissions: ["roles"] }),
    );
  });
});
