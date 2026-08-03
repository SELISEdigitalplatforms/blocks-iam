import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  userResult: {} as Record<string, unknown>,
  rolesResult: {} as Record<string, unknown>,
  permsResult: {} as Record<string, unknown>,
  mutateAsync: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
  lastEditorProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => h.rolesResult,
}));
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => h.permsResult,
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => h.userResult,
  useUpdateUserAccessControl: () => ({ mutateAsync: h.mutateAsync }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("./roles-permissions-pill-editor", () => ({
  RolesPermissionsPillEditor: (props: Record<string, unknown>) => {
    h.lastEditorProps = props;
    return (
      <button onClick={props.onSave as () => void}>save-access</button>
    );
  },
}));

import { SingleOrgAccess } from "./single-org-access";

const loaded = () => {
  h.userResult = {
    data: {
      data: {
        itemId: "u1",
        roles: { default: ["admin"] },
        permissions: { default: ["users:read"] },
      },
    },
    isLoading: false,
  };
  h.rolesResult = {
    data: { data: [{ slug: "admin", name: "Admin", itemId: "r1", description: "" }] },
    isLoading: false,
  };
  h.permsResult = {
    data: { data: [{ name: "users:read", resource: "users:read" }] },
    isLoading: false,
  };
};

beforeEach(() => {
  vi.clearAllMocks();
  h.lastEditorProps = null;
});

describe("SingleOrgAccess", () => {
  it("renders the loading skeleton while data is loading", () => {
    h.userResult = { data: undefined, isLoading: true };
    h.rolesResult = { data: undefined, isLoading: true };
    h.permsResult = { data: undefined, isLoading: true };
    const { container } = render(<SingleOrgAccess userId="u1" projectKey="p1" />);
    expect(container.querySelector(".space-y-4")).not.toBeNull();
    expect(screen.queryByText("save-access")).toBeNull();
  });

  it("renders the pill editor with the hydrated roles once loaded", () => {
    loaded();
    render(<SingleOrgAccess userId="u1" projectKey="p1" />);
    expect(screen.getByText("save-access")).toBeInTheDocument();
    const roles = h.lastEditorProps?.roles as Array<{ slug: string }>;
    expect(roles.map((r) => r.slug)).toContain("admin");
  });

  it("saves and shows a success toast", async () => {
    loaded();
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<SingleOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("save-access"));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ organizationId: "default" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the update fails", async () => {
    loaded();
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "denied" });
    render(<SingleOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("save-access"));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "denied" }));
  });

  it("shows an error toast when the update throws", async () => {
    loaded();
    h.mutateAsync.mockRejectedValue({ errors: "boom" });
    render(<SingleOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("save-access"));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "boom" }));
  });
});
