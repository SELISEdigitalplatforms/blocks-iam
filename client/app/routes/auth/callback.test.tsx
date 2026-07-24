import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ setAuthenticated: vi.fn() }));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useAuthStore: () => ({ setAuthenticated: h.setAuthenticated }),
}));

import LoginCallbackPage from "./callback";

const renderAt = (search: string) =>
  render(
    <MemoryRouter initialEntries={[`/login/callback${search}`]}>
      <LoginCallbackPage />
    </MemoryRouter>,
  );

const originalLocation = window.location;
beforeEach(() => {
  vi.clearAllMocks();
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { ...originalLocation, href: "http://localhost/", origin: "http://localhost" },
  });
});
afterEach(() => {
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: originalLocation,
  });
});

describe("LoginCallbackPage", () => {
  it("posts to the backend callback and redirects to the console on success", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue({ ok: true }) as unknown as typeof fetch;
    global.fetch = fetchMock;

    const { container } = renderAt("?code=abc&state=xyz&tenant_id=t1");
    // The loading splash renders while processing.
    expect(container.querySelector("img")).not.toBeNull();

    await vi.waitFor(() => expect(window.location.href).toBe("/app/console"));
    expect(fetchMock).toHaveBeenCalledWith(
      expect.stringContaining("/api/idp/callback"),
      expect.objectContaining({
        credentials: "include",
        headers: expect.objectContaining({ "X-Blocks-Key": "t1" }),
      }),
    );
    expect(h.setAuthenticated).toHaveBeenCalled();
  });

  it("redirects to login with an error when the callback fails", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false }) as unknown as typeof fetch;
    renderAt("?code=abc&state=xyz");
    await vi.waitFor(() =>
      expect(window.location.href).toBe("/login?error=callback_failed"),
    );
  });

  it("redirects to login on a network error", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("boom")) as unknown as typeof fetch;
    renderAt("?code=abc&state=xyz");
    await vi.waitFor(() =>
      expect(window.location.href).toBe("/login?error=callback_error"),
    );
  });

  it("renders nothing when code and state are absent", () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true }) as unknown as typeof fetch;
    const { container } = renderAt("");
    expect(container.querySelector("img")).toBeNull();
  });
});
