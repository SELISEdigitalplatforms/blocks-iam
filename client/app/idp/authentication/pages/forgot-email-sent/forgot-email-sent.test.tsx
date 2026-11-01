import { render, screen } from "@testing-library/react";
import { useI18nStore } from "@seliseblocks/genesis-os/i18n";
import { useAppSettingsStore } from "@seliseblocks/genesis-os/store";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  buildOIDCNavigationUrl: (path: string) => `${path}?tenant=t1`,
  extractOIDCParams: () => ({}),
  // Used by LoginReturnLink, which this page renders for "Go to login".
  resolveLoginReturnTarget: (isOidc: boolean) => ({
    href: isOidc ? "/oidc/login?tenant=t1" : "/login",
    external: false,
  }),
}));
vi.mock("@blocks-idp/authentication/components/success-confirmation-card-header", () => ({
  SuccessConfirmationCardHeader: () => <div data-testid="card-header" />,
}));
vi.mock("@blocks-idp/authentication/components/success-confirmation-icon", () => ({
  SuccessConfirmationIcon: () => <div data-testid="card-icon" />,
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: undefined }),
}));

import { ForgotEmailSent } from "./forgot-email-sent";

const renderAt = (path: string, email: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <ForgotEmailSent email={email} />
    </MemoryRouter>,
  );

describe("ForgotEmailSent", () => {
  it("shows the email the reset link was sent to", () => {
    renderAt("/forgot-email-sent", "user@example.com");
    expect(screen.getByText("user@example.com")).toBeInTheDocument();
    expect(screen.getByText("Email sent")).toBeInTheDocument();
  });

  it("builds a plain resend link with the email appended", () => {
    renderAt("/forgot-email-sent", "user@example.com");
    const resend = screen.getByRole("link", { name: /Resend password reset email/ });
    expect(resend).toHaveAttribute(
      "href",
      "/forgot-password?email=user%40example.com",
    );
  });

  it("builds an OIDC resend link with the correct separator", () => {
    renderAt("/oidc/forgot-email-sent", "user@example.com");
    const resend = screen.getByRole("link", { name: /Resend password reset email/ });
    expect(resend).toHaveAttribute(
      "href",
      "/oidc/forgot-password?tenant=t1&email=user%40example.com",
    );
  });

  it("falls back to placeholder copy when no email is provided", () => {
    renderAt("/forgot-email-sent", "");
    expect(screen.getByText("your email address")).toBeInTheDocument();
  });
});

describe("iam.password language", () => {
  afterEach(() => {
    useAppSettingsStore.getState().setSettings({ language: "en" });
  });

  it("shows German text when the language is de", () => {
    useI18nStore.getState().setRemote("de", "iam.password", {
      "FORGOT_EMAIL_SENT.EMAIL_SENT": "E-Mail gesendet",
    });
    useAppSettingsStore.getState().setSettings({ language: "de" });
    renderAt("/forgot-email-sent", "user@example.com");
    expect(screen.getByText("E-Mail gesendet")).toBeInTheDocument();
  });
});
