import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { authenticationService } from "./authentication.service";
import { AuthConfiguration } from "./auth-config.service";
import { AUTH_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const mockGetConfigPayload = { projectKey: "project-1" };
const mockGetConfigResponse = { isSuccess: true, errors: null, config: { mfaEnabled: false } };
const mockSaveConfigPayload = { projectKey: "project-1", config: { mfaEnabled: true } };
const mockSaveConfigResponse = { isSuccess: true, errors: null };

describe("authenticationService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("should expose an AuthConfiguration instance", () => {
    expect(authenticationService.configuration).toBeInstanceOf(AuthConfiguration);
  });

  describe("configuration.getConfig", () => {
    it("should GET the config endpoint with the ProjectKey query param", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetConfigResponse);

      const result = await authenticationService.configuration.getConfig(mockGetConfigPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${AUTH_CONFIG_ENDPOINTS.GET_CONFIG}?ProjectKey=${mockGetConfigPayload.projectKey}`,
      );
      expect(result).toEqual(mockGetConfigResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        authenticationService.configuration.getConfig(mockGetConfigPayload),
      ).rejects.toThrow("Network error");
    });
  });

  describe("configuration.saveAuthConfig", () => {
    it("should POST the payload to the update endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSaveConfigResponse);

      const result =
        await authenticationService.configuration.saveAuthConfig(mockSaveConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        AUTH_CONFIG_ENDPOINTS.UPDATE_CONFIG,
        mockSaveConfigPayload,
      );
      expect(result).toEqual(mockSaveConfigResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        authenticationService.configuration.saveAuthConfig(mockSaveConfigPayload),
      ).rejects.toThrow("Network error");
    });
  });
});
