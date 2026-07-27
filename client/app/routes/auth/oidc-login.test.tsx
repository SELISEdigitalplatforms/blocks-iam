import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ getRuntimeEnv: vi.fn() }));

vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: h.getRuntimeEnv }));
vi.mock("@/components/logo", () => ({ Logo: () => <div data-testid="logo" /> }));
vi.mock("@/components/mode-toggle/mode-toggle", () => ({
  ModeToggle: () => <div data-testid="mode-toggle" />,
}));

import OidcLogin from "./oidc-login";

const renderPage = () =>
  render(
    <MemoryRouter>
      <OidcLogin />
    </MemoryRouter>,
  );

const originalLocation = window.location;
beforeEach(() => {
  vi.clearAllMocks();
  h.getRuntimeEnv.mockImplementation((k: string) =>
    k === "BLOCKS_IAM_BASE_URL" ? "https://iam" : k === "BLOCKS_X_BLOCKS_KEY" ? "bk" : "",
  );
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { ...originalLocation, href: "http://localhost/" },
  });
});
afterEach(() => {
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: originalLocation,
  });
});

describe("OidcLogin", () => {
  it("renders the marketing panel and developer resources", () => {
    renderPage();
    expect(screen.getByTestId("logo")).toBeInTheDocument();
    expect(screen.getByText("Build with Blocks")).toBeInTheDocument();
    expect(screen.getByText("React")).toBeInTheDocument();
    expect(screen.getAllByText("Coming soon").length).toBeGreaterThan(0);
  });

  it("redirects to the Authorize endpoint including the blocks key on login", () => {
    renderPage();
    fireEvent.click(screen.getByRole("button", { name: /Log in to your account/i }));
    expect(window.location.href).toContain("https://iam/api/Authentication/Authorize");
    expect(window.location.href).toContain("x-blocks-key=bk");
  });

  it("rotates the animated title on the timer", () => {
    vi.useFakeTimers();
    renderPage();
    expect(() => vi.advanceTimersByTime(2500)).not.toThrow();
    vi.useRealTimers();
  });
});
