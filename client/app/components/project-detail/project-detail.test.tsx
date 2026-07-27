import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IProject } from "@blocks-identifier/models/project.model";

vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));
vi.mock("@/components/masked-text", () => ({
  MaskedText: ({ text }: { text: string }) => <span>{text}</span>,
}));
vi.mock("@/lib/domain", () => ({
  getProjectBlocksApiUrl: () => "https://api.blocks.test",
}));

import { ProjectDetail } from "./project-detail";

const project = {
  name: "My Project",
  tenantId: "tenant-123",
  tenantSlug: "my-project",
  environment: "dev",
  createdDate: "2022-01-01T00:00:00Z",
  lastUpdatedDate: "2022-02-01T00:00:00Z",
} as unknown as IProject;

describe("ProjectDetail", () => {
  it("renders a loading skeleton while loading", () => {
    const { container } = render(<ProjectDetail project={undefined} isLoading={true} />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the project details including the API url and slug", () => {
    render(<ProjectDetail project={project} isLoading={false} />);
    expect(screen.getByText("My Project")).toBeInTheDocument();
    expect(screen.getByText("tenant-123")).toBeInTheDocument();
    expect(screen.getByText("my-project")).toBeInTheDocument();
    expect(screen.getByText("https://api.blocks.test")).toBeInTheDocument();
  });

  it("shows the Production label for a prod environment", () => {
    render(
      <ProjectDetail project={{ ...project, environment: "prod" } as IProject} isLoading={false} />,
    );
    expect(screen.getByRole("button", { name: "Production" })).toBeInTheDocument();
  });
});
