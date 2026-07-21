import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { iamService } from "./iam.service";
import { PermissionService } from "./permission.service";
import { OrganizationService } from "./organization.service";
import { PERMISSION_ENDPOINTS, ORGANIZATION_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const mockSeverityResponse = { isSuccess: true, errors: null, data: [] };
const mockOrganizationsResponse = { isSuccess: true, errors: null, data: [], totalCount: 0 };
const mockGetOrganizationsPayload = { page: 1, pageSize: 20 };

describe("iamService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("should compose permission and organization services", () => {
    expect(iamService.permission).toBeInstanceOf(PermissionService);
    expect(iamService.organization).toBeInstanceOf(OrganizationService);
  });

  describe("permission.getPermissionsSeverity", () => {
    it("should GET the by-severity endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSeverityResponse);

      const result = await iamService.permission.getPermissionsSeverity();

      expect(http.get).toHaveBeenCalledWith(
        PERMISSION_ENDPOINTS.GET_PERMISSIONS_GROUP_BY_SEVERITY,
      );
      expect(result).toEqual(mockSeverityResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(iamService.permission.getPermissionsSeverity()).rejects.toThrow("Network error");
    });
  });

  describe("organization.getOrganizations", () => {
    it("should GET with the correct query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockOrganizationsResponse);

      const result = await iamService.organization.getOrganizations(mockGetOrganizationsPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATIONS}?Page=${mockGetOrganizationsPayload.page}&PageSize=${mockGetOrganizationsPayload.pageSize}`,
      );
      expect(result).toEqual(mockOrganizationsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        iamService.organization.getOrganizations(mockGetOrganizationsPayload),
      ).rejects.toThrow("Network error");
    });
  });
});
