import { fireEvent, render, screen } from "@testing-library/react";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ mfaType: 0, mfaId: "mfa-1", oidcUiConfig: undefined as unknown }));

vi.mock("nuqs", () => ({
  parseAsInteger: { withDefault: (d: number) => ({ _d: d }) },
  parseAsString: { withDefault: (d: string) => ({ _d: d }) },
  useQueryStates: () => [{ mfa_type: h.mfaType, mfa_id: h.mfaId }],
}));
vi.mock("../oidc/sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
vi.mock("./mfa-check-form", () => ({
  MfaCheckFrom: ({ onSessionExpired }: { onSessionExpired?: () => void }) => (
    <button type="button" data-testid="mfa-check-form" onClick={onSessionExpired}>
      expire
    </button>
  ),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { MfaCheck } from "./mfa-check";
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

beforeEach(() => {
  h.mfaType = 0;
  h.mfaId = "mfa-1";
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
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
        branding: { logoUrlLight: null, logoUrlDark: null, brandName: "Acme Identity" },
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

  it.each([1, 2])("swaps the heading and drops the instructions once the sign-in expires (mfa_type %i)", (type) => {
    h.mfaType = type;
    render(<MfaCheck />);
    fireEvent.click(screen.getByTestId("mfa-check-form"));
    expect(screen.getByRole("heading", { name: "Sign-in expired" })).toBeInTheDocument();
    expect(screen.queryByText(/Open your authenticator app/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Check your email for the verification code/)).not.toBeInTheDocument();
  });

  it("treats a link without a challenge as expired", () => {
    h.mfaId = "";
    render(<MfaCheck />);
    expect(screen.getByRole("heading", { name: "Sign-in expired" })).toBeInTheDocument();
    expect(screen.queryByText(/Check your email for the verification code/)).not.toBeInTheDocument();
  });
});

describe("iam.mfa-check language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("shows the remote text for another language", () => {
    useI18nStore.getState().setRemote("xx", "iam.mfa-check", {
      "MFA_CHECK.MESSAGE_EMAIL": "[xx] Check your email for the verification code and enter it here to continue.",
    });
    useAppSettingsStore.getState().setSettings({ language: "xx" });
    render(<MfaCheck />);
    expect(screen.getByText("[xx] Check your email for the verification code and enter it here to continue.")).toBeInTheDocument();
  });
});
