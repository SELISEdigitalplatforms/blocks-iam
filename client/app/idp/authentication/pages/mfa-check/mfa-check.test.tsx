import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ mfaType: 0, oidcUiConfig: undefined as unknown }));

vi.mock("nuqs", () => ({
  parseAsInteger: { withDefault: (d: number) => ({ _d: d }) },
  useQueryStates: () => [{ mfa_type: h.mfaType }],
}));
vi.mock("../oidc/sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
vi.mock("./mfa-check-form", () => ({
  MfaCheckFrom: () => <div data-testid="mfa-check-form" />,
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { MfaCheck } from "./mfa-check";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";

beforeEach(() => {
  h.mfaType = 0;
  h.oidcUiConfig = undefined;
});

describe("MfaCheck", () => {
  it("shows the email instructions by default", () => {
    render(<MfaCheck />);
    expect(screen.getByText(/Check your email for the verification code/)).toBeInTheDocument();
    expect(screen.getByTestId("mfa-check-form")).toBeInTheDocument();
  });

  it("shows the authenticator app instructions for mfa_type 1", () => {
    h.mfaType = 1;
    render(<MfaCheck />);
    expect(screen.getByText(/Open your authenticator app/)).toBeInTheDocument();
  });

  it("renders the tenant-defined MFA heading and brand", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...DEFAULT_OIDC_UI_TEMPLATE,
        branding: { logoUrl: null, brandName: "Acme Identity" },
        pages: {
          ...DEFAULT_OIDC_UI_TEMPLATE.pages,
          mfa: { ...DEFAULT_OIDC_UI_TEMPLATE.pages.mfa, heading: "Confirm your identity" },
        },
      },
    };
    render(<MfaCheck />);
    expect(screen.getByText("Confirm your identity")).toBeInTheDocument();
    expect(screen.getByText("Acme Identity")).toBeInTheDocument();
  });
});
