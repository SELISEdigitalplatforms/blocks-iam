import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IPermission } from "@blocks-idp/iam/models/permission";

vi.mock("./delete-organization-permission", () => ({
  DeleteOrganizationPermission: ({ permission }: { permission: IPermission }) => (
    <button>delete-{permission.resource}</button>
  ),
}));

import { OrganizationPermissionsList } from "./organization-permissions-list";

function perm(overrides: Partial<IPermission>): IPermission {
  return {
    itemId: "id",
    name: "Manage",
    type: 1,
    description: "",
    resource: "res",
    resourceGroup: "Users",
    projectKey: "t1",
    tags: [],
    roles: [],
    dependentPermissions: [],
    isArchived: false,
    isBuiltIn: false,
    language: null,
    organizationIds: [],
    permissionSeverity: 0 as unknown as IPermission["permissionSeverity"],
    ...overrides,
  };
}

describe("OrganizationPermissionsList", () => {
  it("renders a row per permission with name, resource and delete action", () => {
    render(
      <OrganizationPermissionsList
        permissions={[
          perm({ name: "Read Users", resource: "user:read" }),
          perm({ name: "Write Users", resource: "user:write" }),
        ]}
        onDelete={vi.fn()}
        onSave={vi.fn()}
      />,
    );
    expect(screen.getByText("Read Users")).toBeInTheDocument();
    expect(screen.getByText("user:read")).toBeInTheDocument();
    expect(screen.getByText("Write Users")).toBeInTheDocument();
    expect(screen.getByText("delete-user:read")).toBeInTheDocument();
    expect(screen.getByText("delete-user:write")).toBeInTheDocument();
  });

  it("renders the empty state when there are no permissions", () => {
    render(
      <OrganizationPermissionsList permissions={[]} onDelete={vi.fn()} />,
    );
    expect(screen.getByText("No permissions found")).toBeInTheDocument();
  });

  it("renders the column headers", () => {
    render(
      <OrganizationPermissionsList
        permissions={[perm({})]}
        onDelete={vi.fn()}
      />,
    );
    expect(screen.getByText("Name")).toBeInTheDocument();
    expect(screen.getByText("Resource")).toBeInTheDocument();
  });
});
