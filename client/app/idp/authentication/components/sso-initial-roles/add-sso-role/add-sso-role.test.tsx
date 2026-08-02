import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ rolesResult: {} as Record<string, unknown> }));

vi.mock("@seliseblocks/genesis-os", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({ useGetRoles: () => h.rolesResult }));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: {
    SearchInput: ({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) => (
      <input aria-label="search" placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} />
    ),
  },
}));

import { AddSSORole } from "./add-sso-role";

beforeEach(() => {
  vi.clearAllMocks();
  h.rolesResult = {
    data: { data: [{ itemId: "r1", name: "Admin", slug: "admin" }], totalCount: 1 },
    isLoading: false,
  };
});

describe("AddSSORole", () => {
  it("opens the manage-roles dialog and lists roles", async () => {
    render(<AddSSORole roles={[]} onAdd={vi.fn()} />);
    fireEvent.click(screen.getByText("Manage Roles"));
    await waitFor(() => expect(screen.getByText("Manage roles")).toBeInTheDocument());
    expect(screen.getByText("Admin")).toBeInTheDocument();
  });

  it("shows the empty state when no roles are returned", async () => {
    h.rolesResult = { data: { data: [], totalCount: 0 }, isLoading: false };
    render(<AddSSORole roles={[]} onAdd={vi.fn()} />);
    fireEvent.click(screen.getByText("Manage Roles"));
    await waitFor(() => expect(screen.getByText("No roles added")).toBeInTheDocument());
  });

  it("selects a role and calls onAdd", async () => {
    const onAdd = vi.fn();
    render(<AddSSORole roles={[]} onAdd={onAdd} />);
    fireEvent.click(screen.getByText("Manage Roles"));
    await waitFor(() => expect(screen.getByText("Admin")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(onAdd).toHaveBeenCalledWith([expect.objectContaining({ slug: "admin" })]),
    );
  });
});
