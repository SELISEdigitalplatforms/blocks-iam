import { render, screen, act, waitFor } from "@testing-library/react";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";

// The heavy background and mode toggle are irrelevant to the shell's behaviour.
vi.mock("./sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
vi.mock("@/components/mode-toggle/mode-toggle", () => ({
  ModeToggle: () => <button type="button">toggle theme</button>,
}));

import { OidcAuthShell, useOidcAuthAnimation } from "./oidc-auth-shell";
import { OIDC_LOGIN_PANEL } from "./oidc-panel-config";

// A child that surfaces the animation context so we can drive the phases.
function Driver() {
  const anim = useOidcAuthAnimation();
  return (
    <div>
      <span data-testid="phase">{anim?.phase}</span>
      <button type="button" onClick={() => anim?.startAnimation()}>
        start
      </button>
      <button type="button" onClick={() => void anim?.succeedAnimation()}>
        succeed
      </button>
      <button type="button" onClick={() => void anim?.failAnimation("boom")}>
        fail
      </button>
      <button type="button" onClick={() => anim?.resetAnimation()}>
        reset
      </button>
    </div>
  );
}

const renderShell = (props: Partial<React.ComponentProps<typeof OidcAuthShell>> = {}) =>
  render(
    <OidcAuthShell panelConfig={OIDC_LOGIN_PANEL} heading="Sign in to Blocks" {...props}>
      <Driver />
    </OidcAuthShell>,
  );

beforeEach(() => {
  // matchMedia is not implemented in jsdom; the shell reads it on mount.
  vi.stubGlobal(
    "matchMedia",
    vi.fn().mockImplementation((query: string) => ({
      matches: query.includes("min-width"),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
  document.documentElement.classList.remove("dark");
});

describe("OidcAuthShell", () => {
  it("renders the heading, brand label and children, starting idle", () => {
    renderShell();
    expect(screen.getByText("Blocks IAM")).toBeInTheDocument();
    // The heading is split word by word.
    expect(screen.getByText("Sign")).toBeInTheDocument();
    expect(screen.getByText("Blocks")).toBeInTheDocument();
    expect(screen.getByTestId("phase")).toHaveTextContent("idle");
    expect(screen.getByTestId("scifi-bg")).toBeInTheDocument();
  });

  it("renders the default copyright footer when no footerNote is given", () => {
    renderShell();
    expect(
      screen.getByText(/SELISE Digital Platforms\. Secure OIDC flow\./),
    ).toBeInTheDocument();
  });

  it("renders a custom footer note when provided", () => {
    renderShell({ footerNote: <span>custom footer</span> });
    expect(screen.getByText("custom footer")).toBeInTheDocument();
    expect(screen.queryByText(/Secure OIDC flow/)).not.toBeInTheDocument();
  });

  it("moves to submitting when startAnimation is called", () => {
    renderShell();
    act(() => screen.getByText("start").click());
    expect(screen.getByTestId("phase")).toHaveTextContent("submitting");
  });

  it("moves to succeeded and shows the success state after the cascade", async () => {
    vi.useFakeTimers();
    renderShell({ successTitle: "You are in" });

    act(() => screen.getByText("succeed").click());
    expect(screen.getByTestId("phase")).toHaveTextContent("succeeded");

    // Let the success dwell timer complete so the cascade finishes.
    await act(async () => {
      vi.runAllTimers();
    });
    expect(screen.getByText("You are in")).toBeInTheDocument();
  });

  it("moves to failed and passes the error message to the panel", async () => {
    renderShell();
    act(() => screen.getByText("fail").click());
    expect(screen.getByTestId("phase")).toHaveTextContent("failed");
    // The error message surfaces in the nodes panel terminal.
    expect(await screen.findByText(/error: boom/)).toBeInTheDocument();
  });

  it("returns to idle when resetAnimation is called", () => {
    renderShell();
    act(() => screen.getByText("start").click());
    act(() => screen.getByText("reset").click());
    expect(screen.getByTestId("phase")).toHaveTextContent("idle");
  });

  it("reacts to the dark class toggling on the html element", async () => {
    const { container } = renderShell();
    const root = container.querySelector(".oidc-scifi-root") as HTMLElement;
    expect(root.getAttribute("data-theme")).toBe("light");

    await act(async () => {
      document.documentElement.classList.add("dark");
    });
    await waitFor(() => expect(root.getAttribute("data-theme")).toBe("dark"));
  });
});
