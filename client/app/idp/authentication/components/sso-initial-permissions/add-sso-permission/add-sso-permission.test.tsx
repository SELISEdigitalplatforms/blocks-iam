import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ permsResult: {} as Record<string, unknown> }));

vi.mock("@seliseblocks/blocks-kit", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({ useGetPermissions: () => h.permsResult }));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: {
    SearchInput: ({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) => (
      <input aria-label="search" placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} />
    ),
  },
}));

import { AddSSOPermission } from "./add-sso-permission";

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

describe("AddSSOPermission", () => {
  it("disables the trigger when 5 permissions are already assigned", () => {
    const perms = Array.from({ length: 5 }, (_, i) => ({ resource: `r${i}` }));
    render(<AddSSOPermission permissions={perms as never} onAdd={vi.fn()} />);
    expect(screen.getByText("Assign Permissions").closest("button")).toBeDisabled();
  });

  it("opens the dialog and shows the selection counter", async () => {
    render(<AddSSOPermission permissions={[]} onAdd={vi.fn()} />);
    fireEvent.click(screen.getByText("Assign Permissions"));
    await waitFor(() => expect(screen.getByText(/You can select up to 5 permissions/)).toBeInTheDocument());
    expect(screen.getByText("Read")).toBeInTheDocument();
  });

  it("selects permissions and calls onAdd", async () => {
    const onAdd = vi.fn();
    render(<AddSSOPermission permissions={[]} onAdd={onAdd} />);
    fireEvent.click(screen.getByText("Assign Permissions"));
    await waitFor(() => expect(screen.getByText("Read")).toBeInTheDocument());
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(onAdd).toHaveBeenCalledWith([expect.objectContaining({ resource: "users:read" })]),
    );
  });
});
