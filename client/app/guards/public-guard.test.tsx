import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const { navigateMock, authStore } = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  authStore: { isAuthenticated: false },
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => navigateMock };
});
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => authStore),
}));

import { PublicGuard } from "./public-guard";

const renderAt = (entry: string) =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <PublicGuard>
        <div>public-child</div>
      </PublicGuard>
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  authStore.isAuthenticated = false;
});

describe("PublicGuard", () => {
  it("renders children for an unauthenticated visitor", async () => {
    renderAt("/login");
    await waitFor(() => expect(screen.getByText("public-child")).toBeInTheDocument());
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("redirects an authenticated visitor to the console", async () => {
    authStore.isAuthenticated = true;
    renderAt("/login");
    await waitFor(() =>
      expect(navigateMock).toHaveBeenCalledWith("/app/console", { replace: true }),
    );
    expect(screen.queryByText("public-child")).not.toBeInTheDocument();
  });

  it("does not redirect during an SSO callback even when authenticated", async () => {
    authStore.isAuthenticated = true;
    renderAt("/login?code=abc&state=xyz");
    await waitFor(() => expect(screen.getByText("public-child")).toBeInTheDocument());
    expect(navigateMock).not.toHaveBeenCalled();
  });
});
