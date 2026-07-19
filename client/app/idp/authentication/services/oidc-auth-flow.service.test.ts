import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AUTH_ENDPOINTS, AUTH_OIDC_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockOidcFlowCredentialPayload,
  mockOidcFlowCredentialResponse,
  mockOidcFlowAccountRecoverPayload,
  mockOidcFlowAccountRecoverResponse,
  mockRefreshTokenStorage,
  mockRefreshedTokenResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
}));

const MOCK_API_BASE = "https://api.blocks.test";

describe("oidc-auth-flow.service", () => {
  let refreshAccessToken: typeof import("./oidc-auth-flow.service").refreshAccessToken;
  let getOidcCredential: typeof import("./oidc-auth-flow.service").getOidcCredential;
  let accountRecover: typeof import("./oidc-auth-flow.service").accountRecover;

  beforeEach(async () => {
    vi.stubGlobal("fetch", vi.fn());

    const mod = await import("./oidc-auth-flow.service");
    refreshAccessToken = mod.refreshAccessToken;
    getOidcCredential = mod.getOidcCredential;
    accountRecover = mod.accountRecover;
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
    localStorage.clear();
  });

  // ─── refreshAccessToken ─────────────────────────────────────────────────────
  describe("refreshAccessToken", () => {
    it("should POST to the refresh endpoint and resolve the new access token", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockRefreshedTokenResponse),
      } as Response);

      const result = await refreshAccessToken();

      // Refresh is cookie-based (credentials: include); the base URL is empty in
      // tests so the URL is just the refresh endpoint path.
      expect(fetch).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.REFRESH,
        expect.objectContaining({ method: "POST", credentials: "include" }),
      );
      expect(result).toBe(mockRefreshedTokenResponse.access_token);
    });

    it("should return null when the refresh request fails", async () => {
      const result = await refreshAccessToken();
      expect(result).toBeNull();
    });

    it("should return null when response has an error field", async () => {
      localStorage.setItem("oidc-auth-storage", mockRefreshTokenStorage);

      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        json: () => Promise.resolve({ error: "invalid_grant" }),
      } as Response);

      const result = await refreshAccessToken("test-key");
      expect(result).toBeNull();
    });

    it("should return null when fetch fails", async () => {
      localStorage.setItem("oidc-auth-storage", mockRefreshTokenStorage);

      vi.mocked(fetch).mockResolvedValue({
        ok: false,
        status: 400,
      } as Response);

      const result = await refreshAccessToken("test-key");
      expect(result).toBeNull();
    });
  });

  // ─── getOidcCredential ───────────────────────────────────────────────────────
  describe("getOidcCredential", () => {
    it("should fetch OIDC credential with auth headers", async () => {
      localStorage.setItem("oidc-auth-storage", JSON.stringify({ access_token: "test-token" }));

      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        status: 200,
        json: () => Promise.resolve(mockOidcFlowCredentialResponse),
      } as Response);

      const result = await getOidcCredential(mockOidcFlowCredentialPayload);

      expect(fetch).toHaveBeenCalledWith(
        expect.stringContaining(AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT),
        expect.objectContaining({ method: "GET" }),
      );
      expect(result).toEqual(mockOidcFlowCredentialResponse);
    });

    it("should retry with refreshed token on 401", async () => {
      localStorage.setItem("oidc-auth-storage", mockRefreshTokenStorage);

      vi.mocked(fetch)
        .mockResolvedValueOnce({ ok: false, status: 401 } as Response)
        .mockResolvedValueOnce({
          ok: true,
          json: () => Promise.resolve(mockRefreshedTokenResponse),
        } as Response)
        .mockResolvedValueOnce({
          ok: true,
          status: 200,
          json: () => Promise.resolve(mockOidcFlowCredentialResponse),
        } as Response);

      const result = await getOidcCredential(mockOidcFlowCredentialPayload);
      expect(result).toEqual(mockOidcFlowCredentialResponse);
      expect(fetch).toHaveBeenCalledTimes(3);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: false,
        status: 500,
        statusText: "Internal Server Error",
      } as Response);

      await expect(getOidcCredential(mockOidcFlowCredentialPayload)).rejects.toThrow();
    });
  });

  // ─── accountRecover ───────────────────────────────────────────────────────────
  describe("accountRecover", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: true,
        json: () => Promise.resolve(mockOidcFlowAccountRecoverResponse),
      } as Response);

      const result = await accountRecover(mockOidcFlowAccountRecoverPayload);

      expect(fetch).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.RECOVER,
        expect.objectContaining({
          method: "POST",
          body: expect.any(String),
        }),
      );
      expect(result).toEqual(mockOidcFlowAccountRecoverResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(fetch).mockResolvedValue({
        ok: false,
        status: 500,
        statusText: "Internal Server Error",
      } as Response);

      await expect(accountRecover(mockOidcFlowAccountRecoverPayload)).rejects.toThrow();
    });
  });
});
