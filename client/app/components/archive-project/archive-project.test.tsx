import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  navigate: vi.fn(),
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@/hooks/use-project", () => ({
  useDisableProject: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));

import { ArchivedProject } from "./archive-project";

const openAndConfirm = async () => {
  fireEvent.click(screen.getByRole("button", { name: /delete/i }));
  const dialog = (await screen.findByText("Delete this environment?")).closest(
    "[role='dialog']",
  ) as HTMLElement;
  fireEvent.click(within(dialog).getByRole("button", { name: "Delete" }));
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("ArchivedProject", () => {
  it("archives the project and navigates on success", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<MemoryRouter><ArchivedProject /></MemoryRouter>);

    await openAndConfirm();

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledWith({ projectKey: "t1" }));
    expect(h.showSuccessToast).toHaveBeenCalledWith({ description: "Project deleted successfully" });
    expect(h.navigate).toHaveBeenCalledWith("/app/users");
  });

  it("shows an error toast when archiving is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "cannot delete" });
    render(<MemoryRouter><ArchivedProject /></MemoryRouter>);

    await openAndConfirm();

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "cannot delete" }));
    expect(h.navigate).not.toHaveBeenCalled();
  });

  it("shows the mapped error toast when archiving throws with errors", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { project: "locked" } });
    render(<MemoryRouter><ArchivedProject /></MemoryRouter>);

    await openAndConfirm();

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { project: "locked" } }),
    );
  });
});
