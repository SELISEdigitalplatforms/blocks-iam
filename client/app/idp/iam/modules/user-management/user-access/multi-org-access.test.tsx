import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  persisted: "",
  setPersisted: vi.fn(),
  userResult: {} as Record<string, unknown>,
  orgsResult: {} as Record<string, unknown>,
  rolesResult: {} as Record<string, unknown>,
  permsResult: {} as Record<string, unknown>,
  mutateAsync: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
  editorProps: null as Record<string, unknown> | null,
}));

vi.mock("nuqs", async () => {
  const React = await import("react");
  return {
    useQueryState: (_key: string, opts?: { defaultValue?: string }) => {
      const [value, setValue] = React.useState(opts?.defaultValue ?? "");
      return [value, (next: string | null) => setValue(next ?? "")];
    },
  };
});
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({ useGetRoles: () => h.rolesResult }));
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({ useGetPermissions: () => h.permsResult }));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => h.orgsResult,
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => h.userResult,
  useUpdateUserAccessControl: () => ({ mutateAsync: h.mutateAsync }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("../user-memberships/manage-organization-dialog", () => ({
  ManageOrganizationDialog: () => <div data-testid="manage-dialog" />,
}));
vi.mock("../user-memberships/remove-membership", () => ({
  RemoveMembership: () => <div data-testid="remove-membership" />,
}));
vi.mock("./roles-permissions-pill-editor", () => ({
  RolesPermissionsPillEditor: (props: Record<string, unknown>) => {
    h.editorProps = props;
    return <button onClick={props.onSave as () => void}>save-access</button>;
  },
}));

import { MultiOrgAccess } from "./multi-org-access";

const withOrgs = () => {
  h.userResult = {
    data: {
      data: {
        itemId: "u1",
        organizationIds: ["org-1"],
        organizations: [{ organizationId: "org-1", roles: ["admin"], permissions: ["users:read"] }],
      },
    },
    isLoading: false,
  };
  h.orgsResult = {
    data: { organizations: [{ itemId: "org-1", name: "Acme", isDisabled: false }] },
    isLoading: false,
  };
  h.rolesResult = { data: { data: [{ slug: "admin", name: "Admin" }] } };
  h.permsResult = { data: { data: [{ name: "users:read" }] } };
};

beforeEach(() => {
  vi.clearAllMocks();
  h.persisted = "";
  h.editorProps = null;
});

describe("MultiOrgAccess", () => {
  it("shows the loading skeleton while data loads", () => {
    h.userResult = { data: undefined, isLoading: true };
    h.orgsResult = { data: undefined, isLoading: true };
    h.rolesResult = {};
    h.permsResult = {};
    const { container } = render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    expect(container.querySelector(".space-y-4")).not.toBeNull();
  });

  it("shows the empty state when the user has no organizations", () => {
    h.userResult = { data: { data: { itemId: "u1", organizationIds: [] } }, isLoading: false };
    h.orgsResult = { data: { organizations: [] }, isLoading: false };
    h.rolesResult = {};
    h.permsResult = {};
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    expect(screen.getByText("No organizations assigned to this user.")).toBeInTheDocument();
  });

  it("renders the org selector and pill editor when organizations exist", () => {
    withOrgs();
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    expect(screen.getByText("Organization")).toBeInTheDocument();
    expect(screen.getByText("save-access")).toBeInTheDocument();
  });

  it("saves the selection and shows a success toast", async () => {
    withOrgs();
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("save-access"));
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the save fails", async () => {
    withOrgs();
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { org: "denied" } });
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("save-access"));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "denied" }));
  });
});
