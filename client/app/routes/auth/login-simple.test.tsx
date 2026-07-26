import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  getRuntimeEnv: vi.fn(),
  showErrorToast: vi.fn(),
  authState: { isAuthenticated: false },
}));

vi.mock("react-router-dom", () => ({ useNavigate: () => h.navigate }));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: h.getRuntimeEnv }));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: h.showErrorToast }));
vi.mock("@seliseblocks/blocks-kit", () => ({
  useAuthStore: () => h.authState,
}));
vi.mock("@/components/blocks-login-page", () => ({
  BlocksLoginPage: ({
    onLogin,
    isLoading,
  }: {
    onLogin: () => void;
    isLoading?: boolean;
  }) => (
    <button onClick={onLogin} disabled={isLoading}>
      {isLoading ? "loading" : "login"}
    </button>
  ),
}));

import LoginSimplePage from "./login-simple";

const originalLocation = window.location;
beforeEach(() => {
  vi.clearAllMocks();
  h.authState.isAuthenticated = false;
  h.getRuntimeEnv.mockImplementation((k: string) =>
    k === "BLOCKS_X_BLOCKS_KEY" ? "bk" : "cid",
  );
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

describe("LoginSimplePage", () => {
  it("redirects to the console when already authenticated", () => {
    h.authState.isAuthenticated = true;
    render(<LoginSimplePage />);
    expect(h.navigate).toHaveBeenCalledWith("/app/console", { replace: true });
  });

  it("redirects the browser to the authorization URL on success", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      json: () => Promise.resolve({ redirect_uri: "https://idp/authorize" }),
    }) as unknown as typeof fetch;

    render(<LoginSimplePage />);
    fireEvent.click(screen.getByText("login"));

    await waitFor(() => expect(window.location.href).toBe("https://idp/authorize"));
    expect(fetch).toHaveBeenCalledWith(
      expect.stringContaining("/api/idp/initiate"),
      expect.objectContaining({ headers: { "X-Blocks-Key": "bk" } }),
    );
  });

  it("shows an error toast when no redirect_uri is returned", async () => {
    global.fetch = vi.fn().mockResolvedValue({
      json: () => Promise.resolve({}),
    }) as unknown as typeof fetch;

    render(<LoginSimplePage />);
    fireEvent.click(screen.getByText("login"));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "Failed to get authorization URL",
      }),
    );
  });

  it("shows an error toast when the request throws", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network")) as unknown as typeof fetch;
    const spy = vi.spyOn(console, "error").mockImplementation(() => {});

    render(<LoginSimplePage />);
    fireEvent.click(screen.getByText("login"));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "Unable to start login. Please try again.",
      }),
    );
    spy.mockRestore();
  });
});
