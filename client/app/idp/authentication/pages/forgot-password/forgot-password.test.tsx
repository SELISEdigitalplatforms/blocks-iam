import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("../oidc/sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
vi.mock("@/components/mode-toggle/mode-toggle", () => ({
  ModeToggle: () => <div data-testid="mode-toggle" />,
}));
vi.mock("./forgot-password-form", () => ({
  ForgotPasswordForm: () => <div data-testid="forgot-password-form" />,
}));

import { ForgotPassword } from "./forgot-password";

describe("ForgotPassword", () => {
  it("renders the branded shell with the forgot-password form", () => {
    render(<ForgotPassword />);
    expect(screen.getByTestId("scifi-bg")).toBeInTheDocument();
    expect(screen.getByTestId("forgot-password-form")).toBeInTheDocument();
    expect(screen.getByText("Blocks IAM")).toBeInTheDocument();
  });

  it("reflects the dark theme from the document element", () => {
    document.documentElement.classList.add("dark");
    const { container } = render(<ForgotPassword />);
    expect(container.querySelector('[data-theme="dark"]')).not.toBeNull();
    document.documentElement.classList.remove("dark");
  });
});
