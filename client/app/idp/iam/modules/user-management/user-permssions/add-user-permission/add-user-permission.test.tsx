import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  permissionsResult: {} as Record<string, unknown>,
  addPermissions: vi.fn(),
  resources: [] as string[],
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => h.permissionsResult,
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useUserPermissions: () => ({
    isPending: h.isPending,
    addPermissions: h.addPermissions,
    resources: h.resources,
  }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (arg: unknown) => h.showError(arg),
  showSuccessToast: (arg: unknown) => h.showSuccess(arg),
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: {
    SearchInput: ({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) => (
      <input aria-label="search" placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} />
    ),
  },
}));

import { AddUserPermission } from "./add-user-permission";

const renderCmp = () =>
  render(<AddUserPermission userId="u1" projectKey="p1" />);

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.resources = [];
  h.permissionsResult = {
    data: {
      data: [
        { itemId: "perm1", name: "Read Users", resource: "users:read", type: 1 },
        { itemId: "perm2", name: "Edit Users", resource: "users:edit", type: 1 },
      ],
      totalCount: 2,
    },
    isLoading: false,
  };
});

describe("AddUserPermission", () => {
  it("renders the assign trigger enabled when under the cap", () => {
    renderCmp();
    expect(screen.getByText("Assign Permissions")).toBeInTheDocument();
  });

  it("disables the trigger when the user already has 5 permissions", () => {
    h.resources = ["a", "b", "c", "d", "e"];
    renderCmp();
    const trigger = screen.getByText("Assign Permissions").closest("button");
    expect(trigger).toBeDisabled();
  });

  it("opens the dialog, selects a permission and includes it", async () => {
    h.addPermissions.mockResolvedValue({ isSuccess: true });
    renderCmp();
    fireEvent.click(screen.getByText("Assign Permissions"));
    await waitFor(() => expect(screen.getByText("Include Permissions")).toBeInTheDocument());
    const checkbox = screen.getAllByRole("checkbox")[0];
    fireEvent.click(checkbox);
    fireEvent.click(screen.getByRole("button", { name: "Include" }));
    await waitFor(() => expect(h.addPermissions).toHaveBeenCalledWith(["users:read"]));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the include request fails", async () => {
    h.addPermissions.mockResolvedValue({ isSuccess: false, errors: "nope" });
    renderCmp();
    fireEvent.click(screen.getByText("Assign Permissions"));
    await waitFor(() => expect(screen.getByText("Include Permissions")).toBeInTheDocument());
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    fireEvent.click(screen.getByRole("button", { name: "Include" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });
});
