import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ mutateAsync: vi.fn(), isPending: false }));

vi.mock("@/hooks/use-project", () => ({
  useCreateProject: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));

import { AddEnvironmentModal } from "./add-environment-modal";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("AddEnvironmentModal", () => {
  it("lists the available environments minus the preselected ones", () => {
    render(<AddEnvironmentModal preSelectedEnvironments={["dev"]} />);
    expect(screen.queryByText("Development")).toBeNull();
    expect(screen.getByText("Testing")).toBeInTheDocument();
    expect(screen.getByText("Production")).toBeInTheDocument();
  });

  it("disables the Add button until at least one environment is selected", () => {
    render(<AddEnvironmentModal />);
    expect(screen.getByRole("button", { name: "Add" })).toBeDisabled();
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    expect(screen.getByRole("button", { name: "Add" })).toBeEnabled();
  });

  it("creates a project with the sorted selected environments on Add", async () => {
    const onClose = vi.fn();
    h.mutateAsync.mockResolvedValue({});
    render(
      <AddEnvironmentModal
        onClose={onClose}
        tenantGroupId="tg-1"
        projectName="My Project"
      />,
    );
    fireEvent.click(screen.getAllByRole("checkbox")[0]);
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ name: "My Project", tenantGroupId: "tg-1" }),
      ),
    );
    expect(onClose).toHaveBeenCalled();
  });

  it("invokes onClose with an empty array on Cancel", () => {
    const onClose = vi.fn();
    render(<AddEnvironmentModal onClose={onClose} tenantGroupId="tg-1" />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onClose).toHaveBeenCalledWith([]);
  });
});
