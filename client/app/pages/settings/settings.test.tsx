import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  projectsResult: {} as Record<string, unknown>,
  update: vi.fn(),
  isUpdating: false,
  toast: vi.fn(),
  selectedProject: { itemId: "p1", name: "Alpha" } as Record<string, unknown> | null,
  setSelectedProject: vi.fn(),
}));

vi.mock("@seliseblocks/blocks-kit", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return {
    ...actual,
    useProjectStore: () => ({
      selectedProject: h.selectedProject,
      selectedTenantGroup: "tg-1",
      setSelectedProject: h.setSelectedProject,
    }),
  };
});
vi.mock("@/hooks/use-project", () => ({
  useGetProjects: () => h.projectsResult,
  useUpdateTenantGroup: () => ({ mutateAsync: h.update, isPending: h.isUpdating }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: (a: unknown) => h.toast(a) }));

import { SettingsPage } from "./settings";

beforeEach(() => {
  vi.clearAllMocks();
  h.isUpdating = false;
  h.selectedProject = { itemId: "p1", name: "Alpha" };
  h.projectsResult = {
    data: [{ projects: [{ itemId: "p1", name: "Alpha", createdDate: "2021-01-01" }] }],
    isLoading: false,
  };
});

describe("SettingsPage", () => {
  it("renders the project general information", () => {
    render(<SettingsPage />);
    expect(screen.getByText("Project Settings")).toBeInTheDocument();
    expect(screen.getByText("Alpha")).toBeInTheDocument();
    expect(screen.getByText("Free")).toBeInTheDocument();
  });

  it("renders the loading skeleton while projects load", () => {
    h.projectsResult = { data: undefined, isLoading: true };
    const { container } = render(<SettingsPage />);
    expect(container.querySelector(".p-6")).not.toBeNull();
    expect(screen.queryByText("Project Settings")).toBeNull();
  });

  it("opens the edit dialog and saves an updated project name", async () => {
    h.update.mockResolvedValue({});
    render(<SettingsPage />);
    fireEvent.click(screen.getByRole("button", { name: /Edit/ }));
    await waitFor(() => expect(screen.getByText("Edit Project")).toBeInTheDocument());
    fireEvent.input(screen.getByLabelText("Project name"), { target: { value: "Beta Project" } });
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.update).toHaveBeenCalledWith(
        expect.objectContaining({ name: "Beta Project", tenantGroupId: "tg-1" }),
      ),
    );
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ description: "Project name updated successfully" }),
      ),
    );
  });

  it("shows an error toast when the update returns errors", async () => {
    h.update.mockResolvedValue({ errors: { name: "taken" } });
    render(<SettingsPage />);
    fireEvent.click(screen.getByRole("button", { name: /Edit/ }));
    await waitFor(() => expect(screen.getByText("Edit Project")).toBeInTheDocument());
    fireEvent.input(screen.getByLabelText("Project name"), { target: { value: "Beta Project" } });
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ description: "Failed to update project name" }),
      ),
    );
  });
});
