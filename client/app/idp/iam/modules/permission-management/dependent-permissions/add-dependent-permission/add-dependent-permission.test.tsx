import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  permsResult: {} as Record<string, unknown>,
}));

vi.mock("@seliseblocks/genesis-os", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => h.permsResult,
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: {
    SearchInput: ({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) => (
      <input aria-label="search" placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} />
    ),
  },
}));

import { AddDependentPermission } from "./add-dependent-permission";

beforeEach(() => {
  vi.clearAllMocks();
  h.permsResult = {
    data: {
      data: [
        { itemId: "p1", name: "Read", resource: "users:read", type: 1 },
        { itemId: "p2", name: "Write", resource: "users:write", type: 1 },
      ],
      totalCount: 2,
    },
    isLoading: false,
  };
});

describe("AddDependentPermission", () => {
  it("opens the dialog listing available permissions", async () => {
    render(<AddDependentPermission permissionsResource={[]} onAdd={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() => expect(screen.getByText("Assign Permissions")).toBeInTheDocument());
    expect(screen.getByText("Read")).toBeInTheDocument();
  });

  it("selects permissions and calls onAdd with the selection", async () => {
    const onAdd = vi.fn();
    render(<AddDependentPermission permissionsResource={[]} onAdd={onAdd} />);
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() => expect(screen.getByText("Read")).toBeInTheDocument());
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    const dialogButtons = screen.getAllByRole("button", { name: "Add" });
    fireEvent.click(dialogButtons[dialogButtons.length - 1]);
    await waitFor(() =>
      expect(onAdd).toHaveBeenCalledWith([expect.objectContaining({ resource: "users:read" })]),
    );
  });

  it("disables checkboxes for already-assigned resources", async () => {
    render(<AddDependentPermission permissionsResource={["users:read"]} onAdd={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() => expect(screen.getByText("Read")).toBeInTheDocument());
    expect(screen.getAllByRole("checkbox")[0]).toBeDisabled();
  });
});
