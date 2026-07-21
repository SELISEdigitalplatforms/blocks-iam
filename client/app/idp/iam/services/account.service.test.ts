import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { serviceInstances } from "@/lib/http-client";
import { UserAccountService } from "./account.service";
import { ACCOUNT_ENDPOINTS } from "../constants/endpoint.constant";
import {
  mockAccountActivationPayload,
  mockResendActivationPayload,
  mockAccountRecoverPayload,
  mockAccountResetPasswordPayload,
  mockActivationCodeValidationPayload,
  mockActivationCodeValidationResponse,
  mockSuccessResponse,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

describe("UserAccountService", () => {
  let service: UserAccountService;

  beforeEach(() => {
    service = new UserAccountService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── accountActivation ────────────────────────────────────────────────────
  describe("accountActivation", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountActivation(mockAccountActivationPayload);

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.ACTIVATE,
        mockAccountActivationPayload,
        {},
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should set X-Blocks-Key header when tenantId is provided", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.accountActivation({
        ...mockAccountActivationPayload,
        tenantId: "***REMOVED***",
      });

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.ACTIVATE,
        expect.objectContaining({ tenantId: "***REMOVED***" }),
        { "X-Blocks-Key": "***REMOVED***" },
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountActivation(mockAccountActivationPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountResendActivation ──────────────────────────────────────────────
  describe("accountResendActivation", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountResendActivation(mockResendActivationPayload);

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.RESEND_ACTIVATION,
        mockResendActivationPayload,
        {},
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should set X-Blocks-Key header when tenantId is provided", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.accountResendActivation({
        ...mockResendActivationPayload,
        tenantId: "***REMOVED***",
      });

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.RESEND_ACTIVATION,
        expect.objectContaining({ tenantId: "***REMOVED***" }),
        { "X-Blocks-Key": "***REMOVED***" },
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountResendActivation(mockResendActivationPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountRecover ───────────────────────────────────────────────────────
  describe("accountRecover", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountRecover(mockAccountRecoverPayload);

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.RECOVER,
        mockAccountRecoverPayload,
        {},
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should set X-Blocks-Key header when tenantId is provided", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.accountRecover({
        ...mockAccountRecoverPayload,
        tenantId: "***REMOVED***",
      });

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.RECOVER,
        expect.objectContaining({ tenantId: "***REMOVED***" }),
        { "X-Blocks-Key": "***REMOVED***" },
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountRecover(mockAccountRecoverPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountResetPassword ─────────────────────────────────────────────────
  describe("accountResetPassword", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountResetPassword(mockAccountResetPasswordPayload);

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.RESET_PASSWORD,
        mockAccountResetPasswordPayload,
        {},
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountResetPassword(mockAccountResetPasswordPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── checkActivationCodeExpiration ────────────────────────────────────────
  describe("checkActivationCodeExpiration", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockActivationCodeValidationResponse);

      const result = await service.checkActivationCodeExpiration(
        mockActivationCodeValidationPayload,
      );

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE,
        mockActivationCodeValidationPayload,
        {},
      );
      expect(result).toEqual(mockActivationCodeValidationResponse);
    });

    it("should set X-Blocks-Key header when tenantId is provided", async () => {
      vi.mocked(http.post).mockResolvedValue(mockActivationCodeValidationResponse);

      await service.checkActivationCodeExpiration({
        ...mockActivationCodeValidationPayload,
        tenantId: "***REMOVED***",
      });

      expect(http.post).toHaveBeenCalledWith(
        ACCOUNT_ENDPOINTS.VALIDATE_ACTIVATION_CODE,
        expect.objectContaining({ tenantId: "***REMOVED***" }),
        { "X-Blocks-Key": "***REMOVED***" },
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        service.checkActivationCodeExpiration(mockActivationCodeValidationPayload),
      ).rejects.toThrow("Network error");
    });
  });
});
