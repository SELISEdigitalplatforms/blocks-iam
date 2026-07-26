import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  user: { data: { data: { active: true } } } as { data: { data: { active: boolean } } | undefined },
  resetProps: null as Record<string, unknown> | null,
  resendProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => ({ data: h.user.data }),
}));
vi.mock("./user-reset-password", () => ({
  UserResetPassword: (props: Record<string, unknown>) => {
    h.resetProps = props;
    return <div data-testid="reset-modal">{String(props.open)}</div>;
  },
}));
vi.mock("./user-resend-activation/user-resend-activation", () => ({
  UserResendActivationMail: (props: Record<string, unknown>) => {
    h.resendProps = props;
    return <div data-testid="resend-modal">{String(props.open)}</div>;
  },
}));

import { UserActionMenu } from "./user-action-menu";

beforeEach(() => {
  vi.clearAllMocks();
  h.user.data = { data: { active: true } };
});

describe("UserActionMenu", () => {
  it("shows reset-password controls for an active user and opens the reset modal", () => {
    render(<UserActionMenu id="u1" projectKey="p1" />);
    const resetButtons = screen.getAllByRole("button", { name: "Reset Password" });
    expect(resetButtons.length).toBeGreaterThan(0);
    fireEvent.click(resetButtons[0]);
    expect(h.resetProps?.open).toBe(true);
  });

  it("shows resend-activation controls for an inactive user and opens the resend modal", () => {
    h.user.data = { data: { active: false } };
    render(<UserActionMenu id="u1" projectKey="p1" />);
    fireEvent.click(screen.getAllByRole("button", { name: "Resend Activation" })[0]);
    expect(h.resendProps?.open).toBe(true);
  });

  it("treats a missing user as inactive", () => {
    h.user.data = undefined;
    render(<UserActionMenu id="u1" projectKey="p1" />);
    expect(screen.getAllByText("Resend Activation").length).toBeGreaterThan(0);
  });
});
