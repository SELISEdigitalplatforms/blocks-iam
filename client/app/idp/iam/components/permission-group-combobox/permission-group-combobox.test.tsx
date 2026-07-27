import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  resourceGroups: [] as { resourceGroup: string }[],
}));

vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetResourceGroup: () => ({ data: h.resourceGroups }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));

import { PermissionGroupCombobox } from "./permission-group-combobox";

beforeEach(() => {
  vi.clearAllMocks();
  h.resourceGroups = [{ resourceGroup: "Users" }, { resourceGroup: "Roles" }];
});

describe("PermissionGroupCombobox", () => {
  it("shows the placeholder when no value is selected", () => {
    render(<PermissionGroupCombobox value="" onChange={vi.fn()} />);
    expect(screen.getByText("Select or type...")).toBeInTheDocument();
  });

  it("shows the current value on the trigger", () => {
    render(<PermissionGroupCombobox value="Users" onChange={vi.fn()} />);
    expect(screen.getByText("Users")).toBeInTheDocument();
  });

  it("selects an existing resource group from the list", () => {
    const onChange = vi.fn();
    render(<PermissionGroupCombobox value="" onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: "Select or type..." }));
    fireEvent.click(screen.getByText("Roles"));
    expect(onChange).toHaveBeenCalledWith("Roles");
  });

  it("filters the list by the typed value", () => {
    render(<PermissionGroupCombobox value="" onChange={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Select or type..." }));
    fireEvent.change(screen.getByPlaceholderText("Search or type..."), {
      target: { value: "rol" },
    });
    expect(screen.getByText("Roles")).toBeInTheDocument();
    expect(screen.queryByText("Users")).not.toBeInTheDocument();
  });

  it("adds a new typed group through the plus button", () => {
    const onChange = vi.fn();
    render(<PermissionGroupCombobox value="" onChange={onChange} />);
    fireEvent.click(screen.getByRole("button", { name: "Select or type..." }));
    fireEvent.change(screen.getByPlaceholderText("Search or type..."), {
      target: { value: "NewGroup" },
    });
    const buttons = screen.getAllByRole("button");
    fireEvent.click(buttons[buttons.length - 1]);
    expect(onChange).toHaveBeenCalledWith("NewGroup");
  });
});
