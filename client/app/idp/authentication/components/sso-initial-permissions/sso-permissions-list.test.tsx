import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IPermission } from "@blocks-idp/iam/models/permission";

vi.mock("./delete-sso-permission", () => ({
  DeleteSSOPermission: ({ permission }: { permission: IPermission }) => (
    <button>del-{permission.resource}</button>
  ),
}));

import { SSOPermissionsList } from "./sso-permissions-list";

const permissions = [
  { itemId: "p1", name: "Read Users", resource: "users:read" },
  { itemId: "p2", name: "Write Users", resource: "users:write" },
] as IPermission[];

beforeEach(() => {
  vi.clearAllMocks();
});

describe("SSOPermissionsList", () => {
  it("shows the empty state when there are no permissions", () => {
    render(<SSOPermissionsList permissions={[]} onDelete={vi.fn()} />);
    expect(screen.getByText("No permissions found")).toBeInTheDocument();
  });

  it("renders a row per permission with name and resource", () => {
    render(<SSOPermissionsList permissions={permissions} onDelete={vi.fn()} />);
    expect(screen.getByText("Read Users")).toBeInTheDocument();
    expect(screen.getByText("users:read")).toBeInTheDocument();
    expect(screen.getByText("Write Users")).toBeInTheDocument();
    expect(screen.getByText("users:write")).toBeInTheDocument();
  });
});
