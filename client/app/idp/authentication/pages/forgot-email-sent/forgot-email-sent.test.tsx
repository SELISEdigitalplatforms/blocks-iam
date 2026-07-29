import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";

vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  buildOIDCNavigationUrl: (path: string) => `${path}?tenant=t1`,
}));
vi.mock("@blocks-idp/authentication/components/success-confirmation-card-header", () => ({
  SuccessConfirmationCardHeader: () => <div data-testid="card-header" />,
}));
vi.mock("@blocks-idp/authentication/components/success-confirmation-icon", () => ({
  SuccessConfirmationIcon: () => <div data-testid="card-icon" />,
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
