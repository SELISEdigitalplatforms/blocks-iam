import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  verifyAuth: { isSuccess: false } as Record<string, unknown> | undefined,
  github: vi.fn(),
  gitlab: vi.fn(),
  bitbucket: vi.fn(),
  azure: vi.fn(),
  aws: vi.fn(),
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/cross-modules/devops/models/git-dummy", () => ({
  providers: [
    { id: "github", name: "GitHub", icon: "github", active: true },
    { id: "gitlab", name: "GitLab", icon: "gitlab", active: true },
  ],
}));
vi.mock("@/cross-modules/devops/models/github-info", () => ({ iconMap: { github: "/gh.svg", gitlab: "/gl.svg" } }));
vi.mock("@/cross-modules/devops/services/providers.service", () => ({
  authenticateWithGithub: (...a: unknown[]) => h.github(...a),
  authenticateWithGitlab: () => h.gitlab(),
  authenticateWithBitbucket: () => h.bitbucket(),
  authenticateWithAzure: () => h.azure(),
  authenticateWithAws: () => h.aws(),
}));
vi.mock("@/cross-modules/devops/hooks/github-info", () => ({
  useValidateAuthorization: () => ({ data: h.verifyAuth }),
}));
vi.mock("@seliseblocks/blocks-kit", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});

import ProviderButtons from "./render-provider";

const renderButtons = (props: Record<string, unknown> = {}) =>
  render(
    <MemoryRouter>
      <ProviderButtons destination="/devops/configure" {...props} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.verifyAuth = { isSuccess: false };
  localStorage.clear();
});

describe("ProviderButtons", () => {
  it("renders a continue button for each provider", () => {
    renderButtons();
    expect(screen.getByText("Continue with GitHub")).toBeInTheDocument();
    expect(screen.getByText("Continue with GitLab")).toBeInTheDocument();
  });

  it("starts github authentication when not authorized", () => {
    renderButtons();
    fireEvent.click(screen.getByText("Continue with GitHub"));
    expect(h.github).toHaveBeenCalledWith("", "tenant-1");
  });

  it("calls onClose when github is already authorized", () => {
    h.verifyAuth = { isSuccess: true };
    const onClose = vi.fn();
    renderButtons({ onClose });
    fireEvent.click(screen.getByText("Continue with GitHub"));
    expect(onClose).toHaveBeenCalledWith(true);
  });

  it("triggers the gitlab authentication flow", () => {
    renderButtons();
    fireEvent.click(screen.getByText("Continue with GitLab"));
    expect(h.gitlab).toHaveBeenCalled();
  });
});
