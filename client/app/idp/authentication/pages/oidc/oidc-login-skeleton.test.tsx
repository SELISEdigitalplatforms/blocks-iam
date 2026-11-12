import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import {
  OidcLoginFormSkeleton,
  OidcLoginPageSkeleton,
  readRememberedSidePanel,
  rememberSidePanel,
} from "./oidc-login-skeleton";

afterEach(() => localStorage.clear());

describe("OidcLoginPageSkeleton", () => {
  it("announces the wait once and hides the placeholder shapes from assistive tech", () => {
    const { container } = render(<OidcLoginPageSkeleton />);
    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-busy", "true");
    expect(status).toHaveTextContent("Loading sign-in options…");
    expect(container.querySelector("main")).toHaveAttribute("aria-hidden", "true");
  });

  it("lays out the form column and the side panel like the real shell", () => {
    const { container } = render(<OidcLoginPageSkeleton />);
    const card = container.querySelector("main > div");
    expect(card?.children).toHaveLength(2);
    // The panel column only exists from md up, as in OidcAuthShell.
    expect(card?.children[1]).toHaveClass("hidden", "md:block");
  });

  it("fills the card's height with the form column wherever the card stacks", () => {
    const { container } = render(<OidcLoginPageSkeleton />);
    expect(container.querySelector("main > div")?.children[0]).toHaveClass("max-md:flex-1", "md:w-1/2");
  });

  it("takes the single narrow card shape for a tenant without the side panel", () => {
    const { container } = render(<OidcLoginPageSkeleton showSidePanel={false} />);
    const card = container.querySelector("main > div");
    expect(card).toHaveClass("max-w-lg");
    expect(card).not.toHaveClass("md:flex-row");
    expect(card?.children).toHaveLength(1);
    expect(card?.children[0]).toHaveClass("flex-1");
  });

  it("pulses only for visitors who have not asked for reduced motion", () => {
    const { container } = render(<OidcLoginPageSkeleton />);
    expect(container.querySelector(".animate-pulse")).toBeNull();
    expect(container.querySelector(".motion-safe\\:animate-pulse")).not.toBeNull();
  });
});

describe("remembered side-panel setting", () => {
  it("is unknown on a first visit and without a tenant", () => {
    expect(readRememberedSidePanel("tenant-1")).toBeUndefined();
    rememberSidePanel(undefined, false);
    expect(readRememberedSidePanel(undefined)).toBeUndefined();
  });

  it("returns what was last recorded for that tenant only", () => {
    rememberSidePanel("tenant-1", false);
    rememberSidePanel("tenant-2", true);
    expect(readRememberedSidePanel("tenant-1")).toBe(false);
    expect(readRememberedSidePanel("tenant-2")).toBe(true);
    rememberSidePanel("tenant-1", true);
    expect(readRememberedSidePanel("tenant-1")).toBe(true);
  });
});

describe("OidcLoginFormSkeleton", () => {
  it("announces the wait and draws in the tenant palette's border colour", () => {
    const { container } = render(<OidcLoginFormSkeleton />);
    expect(screen.getByRole("status")).toHaveTextContent("Loading sign-in options…");
    expect(container.querySelector(".bg-\\(--border\\)")).not.toBeNull();
  });
});
