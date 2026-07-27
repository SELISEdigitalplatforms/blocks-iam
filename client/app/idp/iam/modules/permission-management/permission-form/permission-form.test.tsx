import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("../dependent-permissions", () => ({
  DependentPermissions: () => <div data-testid="dependent-permissions" />,
}));
vi.mock("@blocks-idp/iam/components/permission-group-combobox/permission-group-combobox", () => ({
  PermissionGroupCombobox: ({ value }: { value: string }) => <div data-testid="group-combobox">{value}</div>,
}));

import { PermissionForm } from "./permission-form";

const values = {
  name: "Read Users",
  type: 1,
  resource: "users:read",
  resourceGroup: "Users",
  permissionSeverity: 1,
  tags: [],
  description: "reads",
  isBuiltIn: false,
  dependentPermissions: [],
} as unknown as Parameters<typeof PermissionForm>[0]["values"];

beforeEach(() => vi.clearAllMocks());

describe("PermissionForm", () => {
  it("renders the form fields prefilled from values", () => {
    render(<PermissionForm onSave={vi.fn()} isPending={false} values={values} />);
    expect((screen.getByPlaceholderText("Enter service::controller::name") as HTMLInputElement).value).toBe(
      "users:read",
    );
    expect(screen.getByText("Severity")).toBeInTheDocument();
  });

  it("shows the dependent-permissions field for resource type 2", () => {
    render(
      <PermissionForm
        onSave={vi.fn()}
        isPending={false}
        values={{ ...values, type: 2 } as never}
      />,
    );
    expect(screen.getByTestId("dependent-permissions")).toBeInTheDocument();
  });

  it("submits the form data via onSave", async () => {
    const onSave = vi.fn();
    // type 2 (non service::controller::name) so the resource-format refine passes.
    render(
      <PermissionForm
        onSave={onSave}
        isPending={false}
        values={{ ...values, type: 2, resource: "usersread" } as never}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ name: "Read Users" })),
    );
  });

  it("disables the save button while pending", () => {
    render(<PermissionForm onSave={vi.fn()} isPending values={values} />);
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });
});
