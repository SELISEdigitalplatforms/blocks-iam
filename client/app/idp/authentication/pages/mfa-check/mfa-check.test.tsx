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
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

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
        ...OIDC_UI_TEMPLATE_FIXTURE,
        branding: { logoUrl: null, brandName: "Acme Identity" },
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          mfa: { ...OIDC_UI_TEMPLATE_FIXTURE.pages.mfa, heading: "Confirm your identity" },
        },
      },
    };
    render(<MfaCheck />);
    expect(screen.getByText("Confirm your identity")).toBeInTheDocument();
    expect(screen.getByText("Acme Identity")).toBeInTheDocument();
  });
});
