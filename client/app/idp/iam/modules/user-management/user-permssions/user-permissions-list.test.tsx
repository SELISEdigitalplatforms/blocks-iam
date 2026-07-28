import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IPermission } from "@blocks-idp/iam/models/permission";
import { UserPermissionsList } from "./user-permissions-list";

const permissions = [
  { itemId: "1", name: "Read Users", resource: "user:read" },
  { itemId: "2", name: "Write Users", resource: "user:write" },
] as IPermission[];

describe("UserPermissionsList", () => {
  it("renders a loading skeleton while loading", () => {
    const { container } = render(
      <UserPermissionsList permissions={[]} isLoading={true} userId="u1" onRemovePermission={vi.fn()} />,
    );
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders each permission with name and resource", () => {
    render(
      <UserPermissionsList
        permissions={permissions}
        isLoading={false}
        userId="u1"
        onRemovePermission={vi.fn()}
      />,
    );
    expect(screen.getByText("Read Users")).toBeInTheDocument();
    expect(screen.getByText("user:read")).toBeInTheDocument();
  });

  it("invokes onRemovePermission with the resource", () => {
    const onRemovePermission = vi.fn();
    render(
      <UserPermissionsList
        permissions={permissions}
        isLoading={false}
        userId="u1"
        onRemovePermission={onRemovePermission}
      />,
    );
    fireEvent.click(screen.getAllByRole("button", { name: "Remove role" })[1]);
    expect(onRemovePermission).toHaveBeenCalledWith("user:write");
  });

  it("shows the empty state when there are no permissions", () => {
    render(
      <UserPermissionsList permissions={[]} isLoading={false} userId="u1" onRemovePermission={vi.fn()} />,
    );
    expect(screen.getByText("No permission found")).toBeInTheDocument();
  });
});
