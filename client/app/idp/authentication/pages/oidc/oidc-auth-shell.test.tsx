import { render, screen, act, waitFor } from "@testing-library/react";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";

// The heavy background is irrelevant to the shell's behaviour.
vi.mock("./sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
import { OidcAuthShell, useOidcAuthAnimation } from "./oidc-auth-shell";
import { OIDC_LOGIN_PANEL } from "./oidc-panel-config";
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

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
    <OidcAuthShell
      panelConfig={OIDC_LOGIN_PANEL}
      theme={OIDC_UI_TEMPLATE_FIXTURE.theme}
      logoUrl={null}
      brandName="Blocks IAM"
      heading="Sign in to Blocks"
      footerNote={<span>footer</span>}
      successTitle="Access Granted"
      successSubtitle="Redirecting…"
      {...props}
    >
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
    expect(screen.getByTestId("blocks-default-logo")).toHaveAttribute(
      "src",
      "https://az-cdn.selise.biz/selisecdn/cdn/blocks/logos/selise_blocks_logo_small.svg",
    );
    expect(screen.getByTestId("blocks-default-logo")).toHaveClass("h-7", "w-auto");
    // The heading is split word by word.
    expect(screen.getByText("Sign")).toBeInTheDocument();
    expect(screen.getByText("Blocks")).toBeInTheDocument();
    expect(screen.getByTestId("phase")).toHaveTextContent("idle");
    expect(screen.getByTestId("scifi-bg")).toBeInTheDocument();
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

  it("applies the stored dark palette CSS variables", () => {
    document.documentElement.classList.add("dark");
    const { container } = renderShell({
      theme: {
        ...OIDC_UI_TEMPLATE_FIXTURE.theme,
        dark: {
          ...OIDC_UI_TEMPLATE_FIXTURE.theme.dark,
          primary: "#123456",
        },
      },
    });
    const root = container.querySelector(".oidc-scifi-root") as HTMLElement;
    expect(root.style.getPropertyValue("--accent")).toBe("#123456");
    expect(root.style.getPropertyValue("--bg")).toBe("#080b14");
    expect(root.style.getPropertyValue("--border")).toBe("#273142");
    expect(root).toHaveAttribute("data-theme", "dark");
  });

  it("renders a custom logo and brand name with the auto, light and dark switcher", () => {
    renderShell({
      logoUrl: "https://example.test/acme.png",
      brandName: "Acme Identity",
    });
    expect(screen.getByRole("img", { name: "Acme Identity logo" })).toHaveAttribute(
      "src",
      "https://example.test/acme.png",
    );
    expect(screen.queryByTestId("blocks-default-logo")).not.toBeInTheDocument();
    expect(screen.getByText("Acme Identity")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Auto" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Light" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Dark" })).toBeInTheDocument();
  });

  it("reacts to the resolved html theme and applies the stored light palette", async () => {
    document.documentElement.classList.add("dark");
    const { container } = renderShell();
    const root = container.querySelector(".oidc-scifi-root") as HTMLElement;

    expect(root).toHaveAttribute("data-theme", "dark");
    expect(root.style.getPropertyValue("--bg")).toBe("#080b14");

    await act(async () => {
      document.documentElement.classList.remove("dark");
    });

    await waitFor(() => expect(root).toHaveAttribute("data-theme", "light"));
    expect(root.style.getPropertyValue("--bg")).toBe("#f7f8fa");
  });
});
