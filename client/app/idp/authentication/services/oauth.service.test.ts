import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { serviceInstances } from "@/lib/http-client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  mockGetSocialLoginPayload,
  mockGetSocialLoginResponse,
  mockSigninBySSOPayload,
  mockSigninBySSOResponse,
} from "../../test-utils/__mocks__";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
import { OAuthService } from "./oauth.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("OAuthService", () => {
  let service: OAuthService;

  beforeEach(() => {
    service = new OAuthService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getSocialLoginEndpoint ───────────────────────────────────────────────
  describe("getSocialLoginEndpoint", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockGetSocialLoginResponse);

      const result = await service.getSocialLoginEndpoint(mockGetSocialLoginPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.SOCIAL_AUTHORIZE,
        mockGetSocialLoginPayload,
      );
      expect(result).toEqual(mockGetSocialLoginResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getSocialLoginEndpoint(mockGetSocialLoginPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── signinBySSO ──────────────────────────────────────────────────────────
  describe("signinBySSO", () => {
    it("should POST SSO code/state as JSON to the social callback endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSigninBySSOResponse);

      const result = await service.signinBySSO(mockSigninBySSOPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_ENDPOINTS.SOCIAL_LOGIN,
        {
          code: mockSigninBySSOPayload.code,
          state: mockSigninBySSOPayload.state,
          clientId: "",
        },
        undefined,
        {
          skipTokenRotation: true,
        },
      );
      expect(result).toEqual(mockSigninBySSOResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.signinBySSO(mockSigninBySSOPayload)).rejects.toThrow("Network error");
    });
  });
});
