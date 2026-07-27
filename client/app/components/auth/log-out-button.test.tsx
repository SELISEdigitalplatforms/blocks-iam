import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  resetProjectStore: vi.fn(),
  setUnAuthenticated: vi.fn(),
  clearTokens: vi.fn(),
  resetSelectedLanguages: vi.fn(),
  queryClientClear: vi.fn(),
}));

vi.mock("@/idp/authentication/hooks/use-auth", () => ({
  useLogout: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@/providers/query-provider", () => ({
  getQueryClient: () => ({ clear: h.queryClientClear }),
}));
vi.mock("@/cross-modules/localization/store/use-language-view-store", () => ({
  useLanguageViewStore: () => ({ resetSelectedLanguages: h.resetSelectedLanguages }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ resetProjectStore: h.resetProjectStore }),
}));
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: () => ({ setUnAuthenticated: h.setUnAuthenticated, clearTokens: h.clearTokens }),
}));

import { LogOutButton } from "./log-out-button";

const originalLocation = window.location;

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { origin: "https://app.test", replace: vi.fn() },
  });
});

afterEach(() => {
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: originalLocation,
  });
});

describe("LogOutButton", () => {
  it("logs out, resets state and redirects to login", async () => {
    h.mutateAsync.mockResolvedValue(undefined);
    render(<LogOutButton />);

    fireEvent.click(screen.getByRole("button", { name: "Logout" }));

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.resetProjectStore).toHaveBeenCalled();
    expect(h.setUnAuthenticated).toHaveBeenCalled();
    expect(h.clearTokens).toHaveBeenCalled();
    expect(h.resetSelectedLanguages).toHaveBeenCalled();
    expect(h.queryClientClear).toHaveBeenCalled();
    await waitFor(() =>
      expect(window.location.replace).toHaveBeenCalledWith("https://app.test/login"),
    );
  });

  it("logs the error and does not redirect when logout fails", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    h.mutateAsync.mockRejectedValue(new Error("failed"));
    render(<LogOutButton />);

    fireEvent.click(screen.getByRole("button", { name: "Logout" }));

    await waitFor(() => expect(errorSpy).toHaveBeenCalled());
    expect(window.location.replace).not.toHaveBeenCalled();
    errorSpy.mockRestore();
  });
});
