import { render, screen, fireEvent } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { BlocksLoginPage } from "./index";

vi.mock("@/components/mode-toggle/mode-toggle", () => ({
  ModeToggle: () => <div data-testid="mode-toggle" />,
}));

beforeEach(() => {
  vi.useFakeTimers();
});
afterEach(() => {
  vi.runOnlyPendingTimers();
  vi.useRealTimers();
});

describe("BlocksLoginPage", () => {
  it("renders the nav links and login action", () => {
    render(<BlocksLoginPage name="blocks-iam" onLogin={vi.fn()} loginLabel="Log in" />);
    expect(screen.getByText("Docs")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Log in" })).toBeInTheDocument();
    expect(screen.getByTestId("mode-toggle")).toBeInTheDocument();
  });

  it("invokes onLogin when the login button is clicked", () => {
    const onLogin = vi.fn();
    render(<BlocksLoginPage name="blocks-iam" onLogin={onLogin} loginLabel="Log in" />);
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    expect(onLogin).toHaveBeenCalled();
  });

  it("shows the redirecting label and disables the button when loading", () => {
    render(<BlocksLoginPage name="blocks-iam" onLogin={vi.fn()} isLoading loginLabel="Log in" />);
    const btn = screen.getByRole("button", { name: "Redirecting…" });
    expect(btn).toBeDisabled();
  });

  it("rotates the animated keyword on the interval", () => {
    render(<BlocksLoginPage name="blocks-iam" onLogin={vi.fn()} />);
    expect(() => {
      vi.advanceTimersByTime(3200);
    }).not.toThrow();
  });

  it("falls back to the first product when the name does not match", () => {
    render(<BlocksLoginPage name="unknown-product" onLogin={vi.fn()} loginLabel="Enter" />);
    expect(screen.getByRole("button", { name: "Enter" })).toBeInTheDocument();
  });
});
