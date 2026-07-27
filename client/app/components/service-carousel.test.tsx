import { render, screen, fireEvent, act } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

vi.mock("framer-motion", () => ({
  AnimatePresence: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  motion: new Proxy(
    {},
    {
      get: () => (props: Record<string, unknown>) => {
        const { children, ...rest } = props as { children?: React.ReactNode };
        // Strip animation-only props that are not valid DOM attributes.
        const { variants, initial, animate, exit, transition, custom, ...domProps } = rest as Record<string, unknown>;
        void variants; void initial; void animate; void exit; void transition; void custom;
        return <div {...(domProps as React.HTMLAttributes<HTMLDivElement>)}>{children}</div>;
      },
    },
  ),
}));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "https://service.test" }));

import { ServiceCarousel } from "./service-carousel";

const renderCarousel = () =>
  render(
    <MemoryRouter>
      <ServiceCarousel />
    </MemoryRouter>,
  );

beforeEach(() => vi.useFakeTimers());
afterEach(() => {
  vi.runOnlyPendingTimers();
  vi.useRealTimers();
});

describe("ServiceCarousel", () => {
  it("renders the first service by default", () => {
    renderCarousel();
    expect(screen.getByText("Blocks Agent Platform")).toBeInTheDocument();
  });

  it("advances to the next service when Next is clicked", () => {
    renderCarousel();
    fireEvent.click(screen.getByLabelText("Next service"));
    expect(screen.getByText("Blocks Cloud Build")).toBeInTheDocument();
  });

  it("wraps to the last service when Previous is clicked on the first", () => {
    renderCarousel();
    fireEvent.click(screen.getByLabelText("Previous service"));
    expect(screen.getByText("Blocks Construct")).toBeInTheDocument();
  });

  it("jumps to a specific slide via the dot control", () => {
    renderCarousel();
    fireEvent.click(screen.getByLabelText("Go to slide 3"));
    expect(screen.getByText("Blocks Data Service")).toBeInTheDocument();
  });

  it("auto-advances on the interval", () => {
    renderCarousel();
    act(() => {
      vi.advanceTimersByTime(5000);
    });
    expect(screen.getByText("Blocks Cloud Build")).toBeInTheDocument();
  });
});
