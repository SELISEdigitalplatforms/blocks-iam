import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  permissionMap: new Map<string, unknown>(),
  changePermissionSelection: vi.fn(),
}));

vi.mock("./role-details-state", () => ({
  useRoleDetailsStore: (selector: (s: unknown) => unknown) =>
    selector({
      permissionMap: h.permissionMap,
      changePermissionSelection: h.changePermissionSelection,
    }),
}));
vi.mock("./permission-selection-utils", () => ({
  isChecked: () => false,
}));
vi.mock("./permission-toggle-card", () => ({
  PermissionToggleCard: ({ permission, onCheckedChange }: { permission: { resource: string }; onCheckedChange: (c: boolean) => void }) => (
    <button onClick={() => onCheckedChange(true)}>toggle-{permission.resource}</button>
  ),
}));

import { RequiredPermissionsDialog } from "./required-permission-dialog";

const permission = {
  itemId: "p1",
  resource: "users:read",
  dependentPermissions: ["users:list"],
} as unknown as Parameters<typeof RequiredPermissionsDialog>[0]["permission"];

beforeEach(() => {
  vi.clearAllMocks();
  h.permissionMap = new Map([
    ["users:list", { itemId: "p2", resource: "users:list", dependentPermissions: [] }],
  ]);
});

describe("RequiredPermissionsDialog", () => {
  it("renders the review title and the dependencies section", () => {
    render(<RequiredPermissionsDialog permission={permission} open onOpenChange={vi.fn()} />);
    expect(screen.getByText("Review Permission Changes")).toBeInTheDocument();
    expect(screen.getByText("Dependencies")).toBeInTheDocument();
    expect(screen.getByText("toggle-users:read")).toBeInTheDocument();
    expect(screen.getByText("toggle-users:list")).toBeInTheDocument();
  });

  it("applies the selection changes and closes on save", () => {
    const onOpenChange = vi.fn();
    render(<RequiredPermissionsDialog permission={permission} open onOpenChange={onOpenChange} />);
    fireEvent.click(screen.getByText("toggle-users:read"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(h.changePermissionSelection).toHaveBeenCalledWith([
      expect.objectContaining({ permissionResource: "users:read", isChecked: true }),
    ]);
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });
});
