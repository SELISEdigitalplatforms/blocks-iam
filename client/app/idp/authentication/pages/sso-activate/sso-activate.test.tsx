import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setAuthenticated: vi.fn(),
  setTokens: vi.fn(),
  showErrorToast: vi.fn(),
  getSocialLoginEndpoint: vi.fn(),
  iamBaseUrl: "https://dev-iam.test",
}));

vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: (key: string) =>
    key === "BLOCKS_IAM_BASE_URL" ? h.iamBaseUrl : "blocks-key",
}));
vi.mock("@/lib/get-api-path", () => ({
  getApiUrl: (base: string, path: string) => `https://api.test/${base}/${path}`,
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
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
    expect(screen.getByText("Blocks IAM")).toBeInTheDocument();
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

  beforeEach(() => {
    sessionStorage.clear();
    h.iamBaseUrl = "https://dev-iam.test";
  });

  it("authenticates and navigates to the console on a successful token exchange", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: "at", refresh_token: "rt" }),
    });
    vi.stubGlobal("fetch", fetchMock);
    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    await waitFor(() => expect(h.setAuthenticated).toHaveBeenCalled());
    expect(h.navigateMock).toHaveBeenCalledWith("/app/console");
    expect(h.setTokens).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  it("stores tokens for a localhost base url", async () => {
    h.iamBaseUrl = "http://localhost:4000";
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => ({ access_token: "at", refresh_token: "rt" }),
    });
    vi.stubGlobal("fetch", fetchMock);
    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    await waitFor(() => expect(h.setTokens).toHaveBeenCalledWith("at", "rt"));
    vi.unstubAllGlobals();
  });

  it("shows the provider block and redirects when using a different account", async () => {
    sessionStorage.setItem("clicked_sso_provider", "google");
    sessionStorage.setItem("clicked_sso_audience", "https://aud.test");
    h.getSocialLoginEndpoint.mockResolvedValue({ providerUrl: "https://google.test/auth" });
    const replace = { href: "" };
    Object.defineProperty(window, "location", { value: replace, configurable: true });
    renderComp();
    const useDifferent = await screen.findByText(/Use a different Google account/);
    fireEvent.click(useDifferent);
    await waitFor(() => expect(h.getSocialLoginEndpoint).toHaveBeenCalled());
  });

  it("errors when using a different account without an audience", async () => {
    sessionStorage.setItem("clicked_sso_provider", "google");
    renderComp();
    fireEvent.click(await screen.findByText(/Use a different Google account/));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("surfaces the endpoint error when the social login lookup fails", async () => {
    sessionStorage.setItem("clicked_sso_provider", "google");
    sessionStorage.setItem("clicked_sso_audience", "https://aud.test");
    h.getSocialLoginEndpoint.mockResolvedValue({ error: "endpoint down" });
    renderComp();
    fireEvent.click(await screen.findByText(/Use a different Google account/));
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "endpoint down" }));
  });

  it("errors when the social login lookup returns no url", async () => {
    sessionStorage.setItem("clicked_sso_provider", "google");
    sessionStorage.setItem("clicked_sso_audience", "https://aud.test");
    h.getSocialLoginEndpoint.mockResolvedValue({});
    renderComp();
    fireEvent.click(await screen.findByText(/Use a different Google account/));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "No redirect URL provided." }),
    );
  });

  it("shows a generic error when the social login lookup throws", async () => {
    sessionStorage.setItem("clicked_sso_provider", "google");
    sessionStorage.setItem("clicked_sso_audience", "https://aud.test");
    h.getSocialLoginEndpoint.mockRejectedValue(new Error("network"));
    renderComp();
    fireEvent.click(await screen.findByText(/Use a different Google account/));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("errors when activating without a code", async () => {
    render(
      <MemoryRouter>
        <SsoActivate oauthParams={{ username: "jane@company.com" } as never} />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Code is missing" }));
  });

  it("shows a session-expired error when the token exchange throws expire", async () => {
    const fetchMock = vi.fn().mockRejectedValue({ message: "token expired" });
    vi.stubGlobal("fetch", fetchMock);
    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "Your session has expired. Please try again.",
      }),
    );
    vi.unstubAllGlobals();
  });

  it("shows a generic error when the token exchange throws otherwise", async () => {
    const fetchMock = vi.fn().mockRejectedValue("weird");
    vi.stubGlobal("fetch", fetchMock);
    renderComp();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(screen.getByRole("button", { name: /continue/i }));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
    vi.unstubAllGlobals();
  });
});
