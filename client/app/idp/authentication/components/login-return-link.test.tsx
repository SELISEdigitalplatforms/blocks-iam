import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it } from "vitest";

import { LoginReturnLink } from "./login-return-link";

/**
 * `resolveLoginReturnTarget` reads the real `window.location` (via extractOIDCParams)
 * while the router supplies the pathname, so drive both: history for the query string
 * IAM's activation links carry, MemoryRouter for the route being rendered.
 */
const renderAt = (pathname: string, search = "") => {
  window.history.replaceState({}, "", `${pathname}${search}`);
  return render(
    <MemoryRouter initialEntries={[`${pathname}${search}`]}>
      <LoginReturnLink className="styled">Back to login</LoginReturnLink>
    </MemoryRouter>,
  );
};

const linkHref = () =>
  screen.getByRole("link", { name: "Back to login" }).getAttribute("href");

afterEach(() => {
  window.history.replaceState({}, "", "/");
});

describe("LoginReturnLink", () => {
  it("sends the user back to the originating application's origin", () => {
    renderAt(
      "/oidc/activate/tenant-1",
      "?code=abc&redirect_uri=https%3A%2F%2Fdzcvil-ehxqx.dev.slsblx.com%2Flogin%2Fcallback",
    );
    expect(linkHref()).toBe("https://dzcvil-ehxqx.dev.slsblx.com");
  });

  it("keeps the user in the OIDC flow when the link still carries a clientId", () => {
    renderAt("/oidc/activate/tenant-1", "?code=abc&clientId=client-1");
    expect(linkHref()).toContain("/oidc/login");
  });

  it("falls back to IAM's own login when there is no redirect_uri and no clientId", () => {
    // /oidc/login without a clientId renders "this sign-in link is missing the
    // application it belongs to" — a dead end, so send them somewhere usable.
    renderAt("/oidc/activate/tenant-1", "?code=abc");
    expect(linkHref()).toBe("/login");
  });

  it("falls back to the plain login outside the OIDC routes", () => {
    renderAt("/activate", "?code=abc");
    expect(linkHref()).toBe("/login");
  });

  it("ignores a redirect_uri that is not an http(s) url", () => {
    renderAt("/oidc/activate/tenant-1", "?redirect_uri=javascript%3Aalert(1)");
    expect(linkHref()).toBe("/login");
  });

  it("passes className through so <Button asChild> keeps its styling", () => {
    renderAt(
      "/oidc/activate/tenant-1",
      "?redirect_uri=https%3A%2F%2Fapp.example.com%2Flogin%2Fcallback",
    );
    expect(screen.getByRole("link", { name: "Back to login" })).toHaveClass("styled");
  });
});
