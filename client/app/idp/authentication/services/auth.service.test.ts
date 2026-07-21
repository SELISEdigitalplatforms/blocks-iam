import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { serviceInstances } from "@/lib/http-client";
import { AuthService } from "./auth.service";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";
import {
  mockSigninPayload,
  mockSigninResponse,
  mockSignupPayload,
  mockSignupResponse,
  mockVerifyMfaPayload,
  mockVerifyMfaResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("AuthService", () => {
  let service: AuthService;

  beforeEach(() => {
    service = new AuthService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── signinByEmail ──────────────────────────────────────────────────────────
  describe("signinByEmail", () => {
    it("should POST JSON credentials to the LOGIN endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSigninResponse);

      const result = await service.signinByEmail(mockSigninPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.LOGIN,
        {
          username: mockSigninPayload.username,
          password: mockSigninPayload.password,
          clientId: mockSigninPayload.clientId || "",
        },
        undefined,
        { skipTokenRotation: true },
      );
      expect(result).toEqual(mockSigninResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.signinByEmail(mockSigninPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── verifyMfa ──────────────────────────────────────────────────────────────
  describe("verifyMfa", () => {
    it("should POST form-encoded MFA payload to the OIDC token endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockVerifyMfaResponse);

      const result = await service.verifyMfa(mockVerifyMfaPayload);

      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.OIDC_TOKEN, expect.any(URLSearchParams), {
        "Content-Type": "application/x-www-form-urlencoded",
      });

      const body = vi.mocked(http.post).mock.calls[0][1] as URLSearchParams;
      expect(body.get("grant_type")).toBe("mfa_code");
      expect(body.get("code")).toBe(mockVerifyMfaPayload.code);
      expect(body.get("mfa_id")).toBe(mockVerifyMfaPayload.mfa_id);
      expect(body.get("mfa_type")).toBe(mockVerifyMfaPayload.mfa_type.toString());
      expect(result).toEqual(mockVerifyMfaResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.verifyMfa(mockVerifyMfaPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── signupByEmail ──────────────────────────────────────────────────────────
  describe("signupByEmail", () => {
    it("should POST to the SIGNUP endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSignupResponse);

      const result = await service.signupByEmail(mockSignupPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.SIGNUP,
        { ...mockSignupPayload, isSsoSignup: false },
        {},
        undefined,
      );
      expect(result).toEqual(mockSignupResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.signupByEmail(mockSignupPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── logout ─────────────────────────────────────────────────────────────────
  describe("logout", () => {
    it("should POST to the LOGOUT endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(undefined);

      await service.logout();

      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.LOGOUT, {});
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.logout()).rejects.toThrow("Network error");
    });
  });

  // ─── signinByEmail (client id side effects) ──────────────────────────────────
  describe("signinByEmail client id handling", () => {
    it("persists clientId to sessionStorage when provided", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSigninResponse);
      await service.signinByEmail({ ...mockSigninPayload, clientId: "client-9" });
      expect(sessionStorage.getItem("blocks-auth-client-id")).toBe("client-9");
    });

    it("removes the stored clientId when none is provided", async () => {
      sessionStorage.setItem("blocks-auth-client-id", "stale");
      vi.mocked(http.post).mockResolvedValue(mockSigninResponse);
      await service.signinByEmail(mockSigninPayload);
      expect(sessionStorage.getItem("blocks-auth-client-id")).toBeNull();
    });

    it("forwards a captchaCode into the request body when present", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSigninResponse);
      await service.signinByEmail({ ...mockSigninPayload, captchaCode: "cap-1" });
      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.LOGIN,
        expect.objectContaining({ captchaCode: "cap-1" }),
        undefined,
        { skipTokenRotation: true },
      );
    });
  });

  // ─── verifyOidc ──────────────────────────────────────────────────────────────
  describe("verifyOidc", () => {
    it("sends only code + state when a state is present", async () => {
      vi.mocked(http.post).mockResolvedValue({ ok: true });
      await service.verifyOidc({ code: "c1", state: "s1", clientId: "ignored" });
      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.TOKEN_EXCHANGE, {
        code: "c1",
        state: "s1",
      });
    });

    it("falls back to legacy fields when no state is provided", async () => {
      vi.mocked(http.post).mockResolvedValue({ ok: true });
      await service.verifyOidc({
        code: "c2",
        clientId: "cid",
        redirectUri: "https://cb",
        codeVerifier: "verifier",
        tenantId: "t1",
      });
      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.TOKEN_EXCHANGE, {
        code: "c2",
        client_id: "cid",
        redirect_uri: "https://cb",
        code_verifier: "verifier",
        tenant_id: "t1",
      });
    });
  });

  // ─── verifySsoConsent ────────────────────────────────────────────────────────
  describe("verifySsoConsent", () => {
    it("posts a form-encoded sso_consent grant to the OIDC token endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue({ ok: true });
      await service.verifySsoConsent("consent-code");
      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.OIDC_TOKEN, expect.any(URLSearchParams), {
        "Content-Type": "application/x-www-form-urlencoded",
      });
      const body = vi.mocked(http.post).mock.calls[0][1] as URLSearchParams;
      expect(body.get("grant_type")).toBe("sso_consent");
      expect(body.get("code")).toBe("consent-code");
    });
  });

  // ─── getLoginOptions ─────────────────────────────────────────────────────────
  describe("getLoginOptions", () => {
    it("GETs the plain endpoint with empty headers when no tenant is given", async () => {
      vi.mocked(http.get).mockResolvedValue({ allowedGrantTypes: [] });
      await service.getLoginOptions();
      expect(http.get).toHaveBeenCalledWith(AUTH_ENDPOINTS.GET_LOGIN_OPTIONS, {}, undefined);
    });

    it("adds the tenant query, blocks-key header, and skip option when a tenant is given", async () => {
      vi.mocked(http.get).mockResolvedValue({ allowedGrantTypes: [] });
      await service.getLoginOptions("tenant-7");
      expect(http.get).toHaveBeenCalledWith(
        `${AUTH_ENDPOINTS.GET_LOGIN_OPTIONS}?tenantId=tenant-7`,
        { "X-Blocks-Key": "tenant-7" },
        { skipBlocksKey: true },
      );
    });
  });

  // ─── impersonation ───────────────────────────────────────────────────────────
  describe("impersonation", () => {
    it("stopImpersonation posts an empty body to the stop endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue({ mode: "root", status: "ok" });
      await service.stopImpersonation();
      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.STOP_IMPERSONATION, {});
    });

    it("startImpersonation posts the payload to the impersonate endpoint", async () => {
      const payload = { targeted_tenant_id: "t-9" };
      vi.mocked(http.post).mockResolvedValue({ ok: true });
      await service.startImpersonation(payload);
      expect(http.post).toHaveBeenCalledWith(AUTH_ENDPOINTS.IMPERSONATE, payload);
    });
  });
});
