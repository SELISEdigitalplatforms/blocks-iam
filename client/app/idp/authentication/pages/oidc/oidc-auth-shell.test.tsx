import { render, screen, act, waitFor } from "@testing-library/react";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";
import { configureI18n } from "@seliseblocks/genesis-os/i18n";

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
      <span data-testid="panel-visible">{String(anim?.animationPanelVisible)}</span>
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
      logoUrlLight={null}
      logoUrlDark={null}
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
  configureI18n({ baseUrl: () => "", blocksKey: () => "" });
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
  it.each([undefined, true])("keeps the existing two-column classes when visibility is %s", (showAnimationPanel) => {
    const { container } = renderShell({ showAnimationPanel });
    const main = container.querySelector("main")!;
    expect(main).toHaveClass("max-w-5xl");
    expect(main.firstElementChild).toHaveAttribute("class", "w-full rounded-3xl overflow-hidden flex flex-col md:flex-row shadow-2xl bg-(--surface)");
    expect(main.firstElementChild?.firstElementChild).toHaveAttribute("class", "w-full md:w-1/2 px-6 pt-5 pb-4 sm:px-7 md:px-8 flex flex-col min-h-0 overflow-y-auto");
    expect(container.querySelectorAll(".oidc-sci-fi-panel-border")).toHaveLength(1);
    expect(screen.getByTestId("panel-visible")).toHaveTextContent("true");
  });

  it("omits the panel, uses a full-width narrow card and keeps the surrounding UI", () => {
    const { container } = renderShell({ showAnimationPanel: false });
    const card = container.querySelector("main > div")!;
    expect(card).toHaveClass("w-full", "max-w-lg", "flex-col");
    expect(card).not.toHaveClass("md:flex-row");
    expect(card.firstElementChild).not.toHaveClass("md:w-1/2");
    expect(container.querySelector(".oidc-sci-fi-panel-border")).toBeNull();
    expect(screen.getByTestId("panel-visible")).toHaveTextContent("false");
    expect(screen.getByTestId("scifi-bg")).toBeInTheDocument();
    expect(screen.getByText("Blocks IAM")).toBeInTheDocument();
    expect(screen.getByText("footer")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Light" })).toBeInTheDocument();
  });

  it("schedules no panel cascade timers and completes success in 400 ms when hidden", async () => {
    vi.useFakeTimers();
    const { container } = renderShell({ showAnimationPanel: false });
    expect(vi.getTimerCount()).toBe(0);
    act(() => screen.getByText("succeed").click());
    expect(vi.getTimerCount()).toBe(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(399); });
    expect(screen.queryByText("Access Granted")).not.toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(screen.getByText("Access Granted")).toBeInTheDocument();
    expect(container.querySelector(".oidc-sci-fi-panel-border")).toBeNull();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("resolves failure after exactly 150 ms when hidden", async () => {
    vi.useFakeTimers();
    const resolved = vi.fn();
    function FailureDriver() {
      const anim = useOidcAuthAnimation();
      return <button onClick={() => void anim?.failAnimation("Rejected").then(resolved)}>reject</button>;
    }
    render(
      <OidcAuthShell panelConfig={OIDC_LOGIN_PANEL} theme={OIDC_UI_TEMPLATE_FIXTURE.theme}
        logoUrlLight={null} logoUrlDark={null} brandName="IAM" heading="Sign in"
        footerNote="footer" successTitle="Done" successSubtitle="Ready" showAnimationPanel={false}>
        <FailureDriver />
      </OidcAuthShell>,
    );
    act(() => screen.getByText("reject").click());
    expect(vi.getTimerCount()).toBe(1);
    await act(async () => { await vi.advanceTimersByTimeAsync(149); });
    expect(resolved).not.toHaveBeenCalled();
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(resolved).toHaveBeenCalledOnce();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("keeps the visible panel mounted on mobile and shows success after 400 ms", async () => {
    vi.useFakeTimers();
    vi.mocked(window.matchMedia).mockImplementation((query) => ({
      matches: false, media: query, onchange: null,
      addEventListener: vi.fn(), removeEventListener: vi.fn(),
      addListener: vi.fn(), removeListener: vi.fn(), dispatchEvent: vi.fn(),
    }));
    const { container } = renderShell();
    expect(container.querySelector(".oidc-sci-fi-panel-border")).toBeInTheDocument();
    expect(screen.getByTestId("panel-visible")).toHaveTextContent("true");
    act(() => screen.getByText("succeed").click());
    await act(async () => { await vi.advanceTimersByTimeAsync(399); });
    expect(screen.queryByText("Access Granted")).not.toBeInTheDocument();
    await act(async () => { await vi.advanceTimersByTimeAsync(1); });
    expect(screen.getByText("Access Granted")).toBeInTheDocument();
  });
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
          buttonText: "#0c1024",
        },
      },
    });
    const root = container.querySelector(".oidc-scifi-root") as HTMLElement;
    expect(root.style.getPropertyValue("--accent")).toBe("#123456");
    expect(root.style.getPropertyValue("--bg")).toBe("#080b14");
    expect(root.style.getPropertyValue("--border")).toBe("#273142");
    expect(root.style.getPropertyValue("--button-text")).toBe("#0c1024");
    expect(root).toHaveAttribute("data-theme", "dark");
  });

  it("renders the light-mode logo and brand name with the auto, light and dark switcher", () => {
    renderShell({
      logoUrlLight: "https://example.test/acme-light.png",
      logoUrlDark: "https://example.test/acme-dark.png",
      brandName: "Acme Identity",
    });
    expect(screen.getByRole("img", { name: "Acme Identity logo" })).toHaveAttribute(
      "src",
      "https://example.test/acme-light.png",
    );
    expect(screen.queryByTestId("blocks-default-logo")).not.toBeInTheDocument();
    expect(screen.getByText("Acme Identity")).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Auto" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Light" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Dark" })).toBeInTheDocument();
  });

  it("renders the dark-mode logo and updates it when the resolved mode changes", async () => {
    document.documentElement.classList.add("dark");
    renderShell({
      logoUrlLight: "https://example.test/acme-light.png",
      logoUrlDark: "https://example.test/acme-dark.png",
      brandName: "Acme Identity",
    });

    const logo = screen.getByRole("img", { name: "Acme Identity logo" });
    expect(logo).toHaveAttribute("src", "https://example.test/acme-dark.png");

    await act(async () => {
      document.documentElement.classList.remove("dark");
    });
    await waitFor(() => expect(logo).toHaveAttribute("src", "https://example.test/acme-light.png"));
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
