import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IRole } from "@blocks-idp/iam/models/role";
import { UserRolesList } from "./user-roles-list";

const roles = [
  { itemId: "1", name: "Admin", slug: "admin" },
  { itemId: "2", name: "Viewer", slug: "viewer" },
] as IRole[];

describe("UserRolesList", () => {
  it("renders a loading skeleton while loading", () => {
    const { container } = render(
      <UserRolesList roles={[]} isLoading={true} userId="u1" projectKey="p1" onRemoveRole={vi.fn()} />,
    );
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders each role with name and slug", () => {
    render(
      <UserRolesList roles={roles} isLoading={false} userId="u1" projectKey="p1" onRemoveRole={vi.fn()} />,
    );
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
    expect(screen.getByText("Viewer")).toBeInTheDocument();
  });

  it("invokes onRemoveRole with the role slug", () => {
    const onRemoveRole = vi.fn();
    render(
      <UserRolesList roles={roles} isLoading={false} userId="u1" projectKey="p1" onRemoveRole={onRemoveRole} />,
    );
    fireEvent.click(screen.getAllByRole("button", { name: "Remove role" })[0]);
    expect(onRemoveRole).toHaveBeenCalledWith("admin");
  });

  it("shows the empty state when there are no roles", () => {
    render(
      <UserRolesList roles={[]} isLoading={false} userId="u1" projectKey="p1" onRemoveRole={vi.fn()} />,
    );
    expect(screen.getByText("No roles found")).toBeInTheDocument();
  });
});
