import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { OidcLoginFormSkeleton, OidcLoginPageSkeleton } from "./oidc-login-skeleton";

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

  it("pulses only for visitors who have not asked for reduced motion", () => {
    const { container } = render(<OidcLoginPageSkeleton />);
    expect(container.querySelector(".animate-pulse")).toBeNull();
    expect(container.querySelector(".motion-safe\\:animate-pulse")).not.toBeNull();
  });
});

describe("OidcLoginFormSkeleton", () => {
  it("announces the wait and draws in the tenant palette's border colour", () => {
    const { container } = render(<OidcLoginFormSkeleton />);
    expect(screen.getByRole("status")).toHaveTextContent("Loading sign-in options…");
    expect(container.querySelector(".bg-\\(--border\\)")).not.toBeNull();
  });
});
