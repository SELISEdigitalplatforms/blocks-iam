import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ oidcUiConfig: undefined as unknown }));

vi.mock("../oidc/sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
vi.mock("./oidc-forgot-password-form", () => ({
  OIDCForgotPasswordForm: () => <div data-testid="oidc-forgot-password-form" />,
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { OIDCForgotPassword } from "./oidc-forgot-password";
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

beforeEach(() => {
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
});

afterEach(() => {
  document.documentElement.classList.remove("dark");
});

describe("OIDCForgotPassword", () => {
  it("renders the branded shell with the OIDC forgot-password form", () => {
    render(<OIDCForgotPassword />);
    expect(screen.getByTestId("scifi-bg")).toBeInTheDocument();
    expect(screen.getByTestId("oidc-forgot-password-form")).toBeInTheDocument();
    expect(screen.getByText("Blocks IAM")).toBeInTheDocument();
  });

  it("uses the stored dark palette when dark mode is resolved", () => {
    document.documentElement.classList.add("dark");
    const { container } = render(<OIDCForgotPassword />);
    const root = container.querySelector(".oidc-scifi-root") as HTMLElement;
    expect(root.style.getPropertyValue("--bg")).toBe("#050510");
    expect(root).toHaveAttribute("data-theme", "dark");
  });

  it("renders tenant-defined branding, theme and footer", () => {
    document.documentElement.classList.add("dark");
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        branding: { logoUrl: "https://example.test/acme.svg", brandName: "Acme Identity" },
        theme: {
          ...OIDC_UI_TEMPLATE_FIXTURE.theme,
          dark: { ...OIDC_UI_TEMPLATE_FIXTURE.theme.dark, background: "#101820" },
        },
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          shared: { footerText: "© {year} Acme Corp" },
        },
      },
    };
    const { container } = render(<OIDCForgotPassword />);
    expect(screen.getByText("Acme Identity")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Acme Identity logo" })).toHaveAttribute(
      "src",
      "https://example.test/acme.svg",
    );
    expect(screen.getByText(`© ${new Date().getFullYear()} Acme Corp`)).toBeInTheDocument();
    expect((container.querySelector(".oidc-scifi-root") as HTMLElement).style.getPropertyValue("--bg")).toBe("#101820");
  });
});
