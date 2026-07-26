import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ mfaType: 0 }));

vi.mock("nuqs", () => ({
  parseAsInteger: { withDefault: (d: number) => ({ _d: d }) },
  useQueryStates: () => [{ mfa_type: h.mfaType }],
}));
vi.mock("../oidc/sci-fi-background-oidc", () => ({
  SciFiBackgroundOidc: () => <div data-testid="scifi-bg" />,
}));
vi.mock("@/components/mode-toggle/mode-toggle", () => ({
  ModeToggle: () => <div data-testid="mode-toggle" />,
}));
vi.mock("./mfa-check-form", () => ({
  MfaCheckFrom: () => <div data-testid="mfa-check-form" />,
}));

import { MfaCheck } from "./mfa-check";

beforeEach(() => {
  h.mfaType = 0;
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
});
