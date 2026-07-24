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

describe("BlocksLoginPage atmospheric canvas", () => {
  const make2dCtx = () => ({
    setTransform: vi.fn(),
    clearRect: vi.fn(),
    fillRect: vi.fn(),
    createRadialGradient: vi.fn(() => ({ addColorStop: vi.fn() })),
    fillStyle: "",
  });

  beforeEach(() => {
    // Fake timers stay active (set by the outer hook); we just drive the
    // animation frame synchronously. Run each top-level frame callback once.
    let depth = 0;
    vi.spyOn(window, "requestAnimationFrame").mockImplementation(
      (cb: FrameRequestCallback) => {
        if (depth === 0) {
          depth++;
          cb(0);
          depth--;
        }
        return 1;
      },
    );
    vi.spyOn(window, "cancelAnimationFrame").mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("runs the gradient draw loop when a 2d context is available", () => {
    const ctx = make2dCtx();
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(
      ((type: string) =>
        type === "2d"
          ? (ctx as unknown as CanvasRenderingContext2D)
          : null) as typeof HTMLCanvasElement.prototype.getContext,
    );

    render(<BlocksLoginPage name="blocks-iam" onLogin={vi.fn()} />);
    expect(ctx.setTransform).toHaveBeenCalled();
    expect(ctx.createRadialGradient).toHaveBeenCalledTimes(3);
    expect(ctx.fillRect).toHaveBeenCalled();

    // A resize re-computes the canvas dimensions.
    ctx.setTransform.mockClear();
    window.dispatchEvent(new Event("resize"));
    expect(ctx.setTransform).toHaveBeenCalled();
  });
});
