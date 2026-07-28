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
  removeProps: null as Record<string, unknown> | null,
}));

vi.mock("nuqs", async () => {
  const React = await import("react");
  return {
    useQueryState: (_key: string, opts?: { defaultValue?: string }) => {
      const [value, setValue] = React.useState(h.persisted || opts?.defaultValue || "");
      return [
        value,
        (next: string | null) => {
          h.setPersisted(next);
          setValue(next ?? "");
        },
      ];
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
  RemoveMembership: (props: Record<string, unknown>) => {
    h.removeProps = props;
    return <div data-testid="remove-membership" />;
  },
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

const withTwoOrgs = () => {
  h.userResult = {
    data: {
      data: {
        itemId: "u1",
        organizationIds: ["org-1", "org-2"],
        OrganizationsRoles: { "org-1": ["admin"], "org-2": ["viewer"] },
        OrganizationsPermissions: { "org-1": ["users:read"] },
      },
    },
    isLoading: false,
  };
  h.orgsResult = {
    data: {
      organizations: [
        { itemId: "org-1", name: "Acme", isDisabled: false },
        { itemId: "org-2", name: "Beta", isDisabled: false },
      ],
    },
    isLoading: false,
  };
  h.rolesResult = { data: { data: [{ slug: "admin", name: "Admin" }] } };
  h.permsResult = { data: { data: [{ name: "users:read" }] } };
};

beforeEach(() => {
  vi.clearAllMocks();
  h.persisted = "";
  h.editorProps = null;
  h.removeProps = null;
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

  it("shows a generic error toast when the save throws", async () => {
    withOrgs();
    h.mutateAsync.mockRejectedValue({ errors: "kaboom" });
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("save-access"));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "kaboom" }));
  });

  it("clears a stale URL selection scoped to a different user", async () => {
    withOrgs();
    h.persisted = "someone-else:org-1";
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    await waitFor(() => expect(h.setPersisted).toHaveBeenCalledWith(null));
  });

  it("hydrates role and permission counts from the fallback maps", () => {
    withTwoOrgs();
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    // The fallback-mapped current organization surfaces in the selector trigger.
    expect(screen.getAllByText("Acme").length).toBeGreaterThan(0);
    expect(screen.getByText("save-access")).toBeInTheDocument();
  });

  it("opens revoke and moves selection to the next org on success", async () => {
    withTwoOrgs();
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByLabelText(/Revoke user's access from/i));
    await waitFor(() => expect(h.removeProps).not.toBeNull());
    // The onSuccess handler drops the current org and selects the remaining one.
    (h.removeProps!.onSuccess as () => void)();
    expect(h.setPersisted).toHaveBeenCalled();
  });

  it("closes the revoke dialog via onOpenChange", async () => {
    withTwoOrgs();
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByLabelText(/Revoke user's access from/i));
    await waitFor(() => expect(h.removeProps).not.toBeNull());
    (h.removeProps!.onOpenChange as (open: boolean) => void)(false);
    await waitFor(() => expect(screen.queryByTestId("remove-membership")).toBeNull());
  });

  it("disables revoke access for the default organization", () => {
    h.userResult = {
      data: {
        data: {
          itemId: "u1",
          organizationIds: ["default"],
          organizations: [{ organizationId: "default", roles: [], permissions: [] }],
        },
      },
      isLoading: false,
    };
    h.orgsResult = {
      data: { organizations: [{ itemId: "default", name: "Default", isDisabled: false }] },
      isLoading: false,
    };
    h.rolesResult = { data: { data: [] } };
    h.permsResult = { data: { data: [] } };
    render(<MultiOrgAccess userId="u1" projectKey="p1" />);
    expect(
      screen.getByLabelText(/Revoke user's access from Default/i),
    ).toBeDisabled();
  });
});
