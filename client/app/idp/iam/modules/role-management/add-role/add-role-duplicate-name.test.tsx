import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useAddRole: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { AddRole } from "./add-role";

const fillAndSubmit = async () => {
  fireEvent.click(screen.getByText("Add Role"));
  await waitFor(() => expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument());
  fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "Manager" } });
  fireEvent.input(screen.getByPlaceholderText("Enter slug"), { target: { value: "manager" } });
  fireEvent.click(screen.getByRole("button", { name: "Add" }));
};

const advisory = (otherOrgs: number, slugConflicts = 0) => ({
  isSuccess: false,
  requiresDuplicateNameConfirmation: true,
  duplicateNameOrganizationCount: otherOrgs,
  slugConflictOrganizationCount: slugConflicts,
  errors: { duplicate_name: "Role_Name_Exists_In_Other_Organizations" },
});

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("AddRole — duplicate-name confirmation", () => {
  it("asks for confirmation when other organizations already use the name", async () => {
    h.mutateAsync.mockResolvedValueOnce(advisory(2));
    render(<AddRole />);

    await fillAndSubmit();

    await waitFor(() =>
      expect(screen.getByText("This name is already used elsewhere")).toBeInTheDocument(),
    );
    expect(
      screen.getByText(/2 other organizations already have a role with this name/),
    ).toBeInTheDocument();
    expect(h.showSuccess).not.toHaveBeenCalled();
  });

  it("says how many organizations will keep their own role instead", async () => {
    h.mutateAsync.mockResolvedValueOnce(advisory(3, 1));
    render(<AddRole />);

    await fillAndSubmit();

    await waitFor(() =>
      expect(
        screen.getByText(/1 of them will keep its own role and will not receive this one/),
      ).toBeInTheDocument(),
    );
  });

  it("uses the singular form for a single organization", async () => {
    h.mutateAsync.mockResolvedValueOnce(advisory(1));
    render(<AddRole />);

    await fillAndSubmit();

    await waitFor(() =>
      expect(
        screen.getByText(/1 other organization already has a role with this name/),
      ).toBeInTheDocument(),
    );
  });

  it("resubmits with the confirmation flag and reports success", async () => {
    h.mutateAsync.mockResolvedValueOnce(advisory(2)).mockResolvedValueOnce({ isSuccess: true });
    render(<AddRole />);

    await fillAndSubmit();
    await waitFor(() => screen.getByText("Create anyway"));
    fireEvent.click(screen.getByText("Create anyway"));

    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(h.mutateAsync).toHaveBeenLastCalledWith(
      expect.objectContaining({ name: "Manager", slug: "manager", confirmDuplicateName: true }),
    );
  });

  it("treats a 400 carrying the marker as the same question", async () => {
    h.mutateAsync.mockRejectedValueOnce({
      status: 400,
      requiresDuplicateNameConfirmation: true,
      duplicateNameOrganizationCount: 4,
      slugConflictOrganizationCount: 0,
      errors: { duplicate_name: "Role_Name_Exists_In_Other_Organizations" },
    });
    render(<AddRole />);

    await fillAndSubmit();

    await waitFor(() =>
      expect(screen.getByText("This name is already used elsewhere")).toBeInTheDocument(),
    );
    expect(h.showError).not.toHaveBeenCalled();
  });

  it("cancelling creates nothing and keeps the entered values", async () => {
    h.mutateAsync.mockResolvedValueOnce(advisory(2));
    render(<AddRole />);

    await fillAndSubmit();
    await waitFor(() => screen.getByText("This name is already used elsewhere"));

    // Scoped to the confirmation itself: the add-role form has a Cancel of its own, and clicking
    // that one would close the whole dialog instead of dismissing the question.
    const confirmation = screen
      .getAllByRole("dialog")
      .find((el) => within(el).queryByText("This name is already used elsewhere") !== null)!;
    fireEvent.click(within(confirmation).getByRole("button", { name: "Cancel" }));

    await waitFor(() =>
      expect(screen.queryByText("This name is already used elsewhere")).not.toBeInTheDocument(),
    );
    expect(h.mutateAsync).toHaveBeenCalledTimes(1);
    expect(h.showSuccess).not.toHaveBeenCalled();
    expect((screen.getByPlaceholderText("Enter name") as HTMLInputElement).value).toBe("Manager");
  });

  it("routes a field-coded server error onto its field without asking anything", async () => {
    h.mutateAsync.mockRejectedValueOnce({
      status: 400,
      errors: { Name: "Role_Name_Already_Exists_In_Organization" },
    });
    render(<AddRole />);

    await fillAndSubmit();

    await waitFor(() =>
      expect(screen.getByText("Role_Name_Already_Exists_In_Organization")).toBeInTheDocument(),
    );
    expect(screen.queryByText("This name is already used elsewhere")).not.toBeInTheDocument();
    expect(h.showError).not.toHaveBeenCalled();
  });

  it("still toasts an error that belongs to no field", async () => {
    h.mutateAsync.mockRejectedValueOnce({
      status: 400,
      errors: { forbidden: "Multi_Org_Required_For_Organization_Role" },
    });
    render(<AddRole />);

    await fillAndSubmit();

    await waitFor(() => expect(h.showError).toHaveBeenCalled());
    expect(screen.queryByText("This name is already used elsewhere")).not.toBeInTheDocument();
  });

  it("creates in one step when no other organization uses the name", async () => {
    h.mutateAsync.mockResolvedValueOnce({ isSuccess: true, itemId: "r1" });
    render(<AddRole />);

    await fillAndSubmit();

    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(screen.queryByText("This name is already used elsewhere")).not.toBeInTheDocument();
    expect(h.mutateAsync).toHaveBeenCalledTimes(1);
  });
});
