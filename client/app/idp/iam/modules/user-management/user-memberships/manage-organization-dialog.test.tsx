import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  userData: undefined as unknown,
  rolesData: undefined as unknown,
  permissionsData: undefined as unknown,
  updateMutate: vi.fn(),
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => ({ data: h.userData }),
  useUpdateUserAccessControl: () => ({ mutateAsync: h.updateMutate, isPending: false }),
}));
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => ({ data: h.rolesData }),
}));
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => ({ data: h.permissionsData }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({
    selectedProject: { tenantId: "t1", itemId: "p1" },
    selectedTenantGroup: "tg1",
  })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("./organization-roles-field", () => ({
  OrganizationRolesField: () => <div>roles-field</div>,
}));
vi.mock("./organization-permissions-field", () => ({
  OrganizationPermissionsField: () => <div>permissions-field</div>,
}));

import { ManageOrganizationDialog } from "./manage-organization-dialog";

const org = (over: Record<string, unknown>) =>
  ({ itemId: "org-1", name: "Acme", isDisabled: false, ...over }) as never;

const baseProps = () => ({
  open: true,
  onOpenChange: vi.fn(),
  userId: "u1",
  organizations: [org({ itemId: "org-1", name: "Acme" })],
  isOrgsLoading: false,
});

beforeEach(() => {
  vi.clearAllMocks();
  h.userData = undefined;
  h.rolesData = undefined;
  h.permissionsData = undefined;
});

describe("ManageOrganizationDialog", () => {
  it("renders the dialog header and organization field when open", () => {
    render(<ManageOrganizationDialog {...baseProps()} />, { wrapper: createWrapper() });
    expect(screen.getByText("Manage organization")).toBeInTheDocument();
    expect(
      screen.getByText(/Choose an organization and the roles and permissions/),
    ).toBeInTheDocument();
    expect(screen.getByText("Organization")).toBeInTheDocument();
  });

  it("disables Confirm when the preselected org has no roles", () => {
    render(<ManageOrganizationDialog {...baseProps()} initialOrganizationId="default" />, {
      wrapper: createWrapper(),
    });
    expect(screen.getByRole("button", { name: /confirm/i })).toBeDisabled();
  });

  it("submits the resolved roles/permissions and toasts on success", async () => {
    h.userData = {
      data: {
        organizations: [
          { organizationId: "org-1", roles: ["admin"], permissions: ["read"] },
        ],
      },
    };
    h.rolesData = { data: [{ slug: "admin", name: "Admin", itemId: "r1", description: "" }] };
    h.permissionsData = {
      data: [{ name: "read", itemId: "p1", resource: "read", resourceGroup: "G" }],
    };
    h.updateMutate.mockResolvedValue({ isSuccess: true });

    const props = baseProps();
    render(
      <ManageOrganizationDialog {...props} initialOrganizationId="org-1" />,
      { wrapper: createWrapper() },
    );

    const confirm = screen.getByRole("button", { name: /confirm/i });
    await waitFor(() => expect(confirm).not.toBeDisabled());
    fireEvent.click(confirm);

    await waitFor(() =>
      expect(h.updateMutate).toHaveBeenCalledWith({
        roles: ["admin"],
        permissions: ["read"],
        organizationId: "org-1",
      }),
    );
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Organization managed successfully",
    });
    expect(props.onOpenChange).toHaveBeenCalledWith(false);
  });

  it("closes the dialog when Cancel is clicked", () => {
    const props = baseProps();
    render(<ManageOrganizationDialog {...props} initialOrganizationId="org-1" />, {
      wrapper: createWrapper(),
    });
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(props.onOpenChange).toHaveBeenCalledWith(false);
  });
});
