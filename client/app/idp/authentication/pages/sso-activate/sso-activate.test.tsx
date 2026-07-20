import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setAuthenticated: vi.fn(),
  setTokens: vi.fn(),
  showErrorToast: vi.fn(),
  getSocialLoginEndpoint: vi.fn(),
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: h.showErrorToast }));
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({
    setAuthenticated: h.setAuthenticated,
    setTokens: h.setTokens,
  })),
}));
vi.mock("@blocks-idp/authentication/services/oauth.service", () => ({
  oauthService: { getSocialLoginEndpoint: h.getSocialLoginEndpoint },
}));

import { SsoActivate } from "./sso-activate";

const renderComp = () =>
  render(
    <MemoryRouter>
      <SsoActivate oauthParams={{ code: "auth-code", username: "jane@company.com" }} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("SsoActivate", () => {
  it("renders the card with the terms checkbox and a disabled continue button", () => {
    renderComp();
    expect(screen.getByText("Blocks Cloud")).toBeInTheDocument();
    expect(screen.getByText(/I agree to the/i)).toBeInTheDocument();
    expect(screen.getByRole("checkbox")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /continue/i })).toBeDisabled();
  });

  it("enables continue after accepting terms and posts the token request", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      json: async () => ({ errors: "activation failed" }),
    });
    vi.stubGlobal("fetch", fetchMock);

    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));

    const continueBtn = screen.getByRole("button", { name: /continue/i });
    await waitFor(() => expect(continueBtn).not.toBeDisabled());
    fireEvent.click(continueBtn);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url] = fetchMock.mock.calls[0];
    expect(String(url)).toContain("Authentication/Token");
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "activation failed" }),
    );

    vi.unstubAllGlobals();
  });
});
