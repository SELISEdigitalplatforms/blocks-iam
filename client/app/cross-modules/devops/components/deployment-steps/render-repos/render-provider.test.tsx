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
    { id: "bitbucket", name: "Bitbucket", icon: "bitbucket", active: true },
    { id: "azure", name: "Azure", icon: "azure", active: true },
    { id: "aws", name: "AWS", icon: "aws", active: true },
    { id: "mystery", name: "Mystery", icon: "github", active: true },
  ],
}));
vi.mock("@/cross-modules/devops/models/github-info", () => ({
  iconMap: {
    github: "/gh.svg",
    gitlab: "/gl.svg",
    bitbucket: "/bb.svg",
    azure: "/az.svg",
    aws: "/aws.svg",
  },
}));
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

  it("navigates to the destination when github is authorized and no onClose is given", () => {
    h.verifyAuth = { isSuccess: true };
    renderButtons();
    fireEvent.click(screen.getByText("Continue with GitHub"));
    expect(h.navigate).toHaveBeenCalledWith("/devops/configure");
  });

  it("closes via the storage reload listener after github authentication", () => {
    const onClose = vi.fn();
    renderButtons({ onClose });
    fireEvent.click(screen.getByText("Continue with GitHub"));
    expect(h.github).toHaveBeenCalled();

    const event = new StorageEvent("storage", { key: "isReload", newValue: "true" });
    window.dispatchEvent(event);

    expect(localStorage.getItem("isReload")).toBe("false");
    expect(onClose).toHaveBeenCalledWith(true);
  });

  it("triggers bitbucket, azure and aws flows", () => {
    renderButtons();
    fireEvent.click(screen.getByText("Continue with Bitbucket"));
    expect(h.bitbucket).toHaveBeenCalled();
    fireEvent.click(screen.getByText("Continue with Azure"));
    expect(h.azure).toHaveBeenCalled();
    fireEvent.click(screen.getByText("Continue with AWS"));
    expect(h.aws).toHaveBeenCalled();
  });

  it("logs an error for an unknown provider", () => {
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});
    renderButtons();
    fireEvent.click(screen.getByText("Continue with Mystery"));
    expect(spy).toHaveBeenCalledWith("Unknown provider:", "mystery");
    spy.mockRestore();
  });

  it("calls onClose immediately when closeOnProviderSelect is set", () => {
    const onClose = vi.fn();
    renderButtons({ onClose, closeOnProviderSelect: true });
    fireEvent.click(screen.getByText("Continue with GitLab"));
    expect(onClose).toHaveBeenCalled();
  });
});
