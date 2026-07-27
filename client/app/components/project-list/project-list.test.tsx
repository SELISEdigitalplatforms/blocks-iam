import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  projectsResult: {} as Record<string, unknown>,
  projectResult: {} as Record<string, unknown>,
  selectedProject: { itemId: "p1", name: "Alpha" } as Record<string, unknown> | null,
  storedProjects: [] as unknown[],
  setSelectedProject: vi.fn(),
}));

vi.mock("@/hooks/use-project", () => ({
  useGetProjects: () => h.projectsResult,
  useGetProject: () => h.projectResult,
}));
vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({
    selectedProject: h.selectedProject,
    setSelectedProject: h.setSelectedProject,
    projects: h.storedProjects,
  }),
}));

import { ProjectList } from "./project-list";

const renderList = (collapsed = false) =>
  render(
    <MemoryRouter>
      <ProjectList collapsed={collapsed} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.selectedProject = { itemId: "p1", name: "Alpha" };
  h.projectsResult = {
    data: [{ projects: [{ itemId: "p1", name: "Alpha" }, { itemId: "p2", name: "Beta" }] }],
    isLoading: false,
  };
  h.projectResult = { data: { data: { name: "Alpha" } } };
  h.storedProjects = [];
});

describe("ProjectList", () => {
  it("renders the selected project name in the expanded trigger", () => {
    renderList(false);
    expect(screen.getByText("Alpha")).toBeInTheDocument();
    expect(screen.getByText("Project")).toBeInTheDocument();
  });

  it("renders the collapsed trigger with a tooltip label", () => {
    renderList(true);
    expect(screen.getByText("Alpha")).toBeInTheDocument();
  });

  it("falls back to a placeholder when no project is selected", () => {
    h.selectedProject = null;
    h.projectResult = { data: undefined };
    renderList(false);
    expect(screen.getByText("Select a Project")).toBeInTheDocument();
  });
});
