import { StrictMode } from "react";
import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { OidcAppHandoff } from "./oidc-app-handoff";

const APP_URL = "com.klaxai.app://login/callback?code=abc&state=xyz";
const originalLocation = window.location;
let assignments: string[] = [];

beforeEach(() => {
  assignments = [];
  Object.defineProperty(window, "location", {
    configurable: true,
    value: {
      origin: "https://iam.example.com",
      get href() {
        return assignments[assignments.length - 1] ?? "https://iam.example.com/oidc/login";
      },
      set href(value: string) {
        assignments.push(value);
      },
    },
  });
});

afterEach(() => {
  Object.defineProperty(window, "location", { configurable: true, value: originalLocation });
});

describe("OidcAppHandoff", () => {
  it("renders the design-contract block with the redirect URL on the link (H7)", () => {
    const { container } = render(<OidcAppHandoff url={APP_URL} />);

    const status = screen.getByRole("status");
    expect(status).toHaveAttribute("aria-live", "polite");
    expect(status).toHaveClass(
      "flex",
      "flex-col",
      "items-center",
      "gap-3",
      "py-2",
      "text-center",
      "oidc-animate-fade-up",
    );

    const circle = status.firstElementChild as HTMLElement;
    expect(circle).toHaveClass("w-12", "h-12", "rounded-full", "flex", "items-center", "justify-center");
    expect(circle.style.background).toBe("var(--success-soft)");
    expect(circle.style.border).toBe("1px solid var(--success-border)");

    const icon = circle.querySelector("svg") as SVGElement;
    expect(icon).toHaveAttribute("aria-hidden", "true");
    expect(icon).toHaveAttribute("width", "22");
    expect(icon.style.color).toBe("var(--success)");

    const heading = screen.getByRole("heading", { level: 2, name: "Signed in" });
    expect(heading).toHaveClass("oidc-font-orbitron", "text-lg");
    expect(heading.style.color).toBe("var(--fg)");

    const body = screen.getByText(
      "Returning you to the app. If it does not open, tap the button below.",
    );
    expect(body.tagName).toBe("P");
    expect(body).toHaveClass("text-sm", "leading-relaxed");
    expect(body.style.color).toBe("var(--muted)");
    expect(body.style.fontFamily).toBe("system-ui, sans-serif");

    const link = screen.getByRole("link", { name: "Return to the app" });
    expect(link).toHaveAttribute("href", APP_URL);
    expect(link.className).toBe(
      "oidc-sci-fi-btn inline-block w-full px-5 py-2.5 text-center no-underline",
    );

    // Only theme tokens: no hex or rgb literals in inline styles.
    expect(container.innerHTML).not.toMatch(/#[0-9a-f]{3,8}\b|rgba?\(/i);
  });

  it("opens the app once on mount and moves focus to the link (H6, H7)", () => {
    render(<OidcAppHandoff url={APP_URL} />);
    expect(assignments).toEqual([APP_URL]);
    expect(screen.getByRole("link", { name: "Return to the app" })).toHaveFocus();
  });

  it("assigns the location only once under StrictMode's double effect run (C18)", () => {
    render(
      <StrictMode>
        <OidcAppHandoff url={APP_URL} />
      </StrictMode>,
    );
    expect(assignments).toEqual([APP_URL]);
  });

  it("does not navigate again on re-render (C18)", () => {
    const { rerender } = render(<OidcAppHandoff url={APP_URL} />);
    rerender(<OidcAppHandoff url={APP_URL} />);
    rerender(<OidcAppHandoff url={`${APP_URL}&x=1`} />);
    expect(assignments).toEqual([APP_URL]);
  });

  it("never navigates to a value that is not an app-scheme URI", () => {
    render(<OidcAppHandoff url="javascript:alert(1)" />);
    expect(assignments).toEqual([]);
  });
});
