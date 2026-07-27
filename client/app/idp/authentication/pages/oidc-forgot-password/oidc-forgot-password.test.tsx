import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../oidc/sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
vi.mock("@/components/mode-toggle/mode-toggle", () => ({
  ModeToggle: () => <div data-testid="mode-toggle" />,
}));
vi.mock("./oidc-forgot-password-form", () => ({
  OIDCForgotPasswordForm: () => <div data-testid="oidc-forgot-password-form" />,
}));

import { OIDCForgotPassword } from "./oidc-forgot-password";

describe("OIDCForgotPassword", () => {
  it("renders the branded shell with the OIDC forgot-password form", () => {
    render(<OIDCForgotPassword />);
    expect(screen.getByTestId("scifi-bg")).toBeInTheDocument();
    expect(screen.getByTestId("oidc-forgot-password-form")).toBeInTheDocument();
    expect(screen.getByText("Blocks IAM")).toBeInTheDocument();
  });

  it("reflects the dark theme from the document element", () => {
    document.documentElement.classList.add("dark");
    const { container } = render(<OIDCForgotPassword />);
    expect(container.querySelector('[data-theme="dark"]')).not.toBeNull();
    document.documentElement.classList.remove("dark");
  });
});
