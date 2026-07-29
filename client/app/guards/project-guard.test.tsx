import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const { navigateMock, projectStore, getProjectsResult } = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  projectStore: {
    selectedProject: null as null | { itemId: string },
    selectedTenantGroup: "tg-1" as string | null,
  },
  getProjectsResult: { data: undefined as unknown },
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => navigateMock };
});
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => projectStore),
}));
vi.mock("@/hooks/use-project", () => ({
  useGetProjects: vi.fn(() => getProjectsResult),
}));

import { ProjectGuard } from "./project-guard";

const renderGuard = () =>
  render(
    <MemoryRouter>
      <ProjectGuard>
        <div>project-child</div>
      </ProjectGuard>
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  projectStore.selectedProject = null;
  projectStore.selectedTenantGroup = "tg-1";
  getProjectsResult.data = undefined;
});

describe("ProjectGuard", () => {
  it("redirects to /app/users and renders nothing when no project is selected", async () => {
    renderGuard();
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/app/users", { replace: true }),
    );
    expect(screen.queryByText("project-child")).not.toBeInTheDocument();
  });

  it("renders children when a project is selected and environments exist", async () => {
    projectStore.selectedProject = { itemId: "p1" };
    getProjectsResult.data = [{ projects: [{ itemId: "p1" }] }];
    renderGuard();
    await waitFor(() => expect(screen.getByText("project-child")).toBeInTheDocument());
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("redirects when a project is selected but the environment list is empty", async () => {
    projectStore.selectedProject = { itemId: "p1" };
    getProjectsResult.data = [];
    renderGuard();
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/app/users", { replace: true }),
    );
  });
});
