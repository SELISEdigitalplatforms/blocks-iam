import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  orgConfig: {} as Record<string, unknown>,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

// The tooltip ui-kit re-exports Tooltip* from blocks-kit (aliased to the test
// stub), so keep the stub's exports and only override useProjectStore.
vi.mock("@seliseblocks/genesis-os", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useSaveOrganization: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
  useGetOrganizationConfig: () => ({ data: h.orgConfig }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { AddOrganization } from "./add-organization";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.orgConfig = { isMultiOrgEnabled: true, allowOrgCreationFromCloud: true };
});

describe("AddOrganization", () => {
  it("enables the add trigger when org creation is allowed", () => {
    render(<AddOrganization />);
    expect(screen.getByText("Add Organization").closest("button")).not.toBeDisabled();
  });

  it("disables the add trigger when org creation from cloud is not enabled", () => {
    h.orgConfig = { isMultiOrgEnabled: true, allowOrgCreationFromCloud: false };
    render(<AddOrganization />);
    expect(screen.getByText("Add Organization").closest("button")).toBeDisabled();
  });

  it("adds an organization and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<AddOrganization />);
    fireEvent.click(screen.getByText("Add Organization"));
    await waitFor(() =>
      expect(screen.getByPlaceholderText("Enter organization name")).toBeInTheDocument(),
    );
    fireEvent.input(screen.getByPlaceholderText("Enter organization name"), {
      target: { value: "Acme Inc" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith({ name: "Acme Inc", createdFrom: 1 }),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when creation fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "duplicate" });
    render(<AddOrganization />);
    fireEvent.click(screen.getByText("Add Organization"));
    await waitFor(() =>
      expect(screen.getByPlaceholderText("Enter organization name")).toBeInTheDocument(),
    );
    fireEvent.input(screen.getByPlaceholderText("Enter organization name"), {
      target: { value: "Acme Inc" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "duplicate" }));
  });
});
