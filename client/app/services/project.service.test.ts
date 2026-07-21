import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { projectService } from "./project.service";
import { PROJECT_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const mockProjectGroups = [{ tenantGroupId: "group-1", projects: [] }];
const mockProjectResponse = { isSuccess: true, errors: null, project: { itemId: "project-1" } };

describe("ProjectService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  describe("getProjects", () => {
    it("should GET with default paging as an absolute URL", async () => {
      vi.mocked(http.get).mockResolvedValue(mockProjectGroups);

      const result = await projectService.getProjects();

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GETS}?page=0&pageSize=100&tenantGroupId=`,
        undefined,
        { absoluteUrl: true },
      );
      expect(result).toEqual(mockProjectGroups);
    });

    it("should forward explicit paging and tenantGroupId", async () => {
      vi.mocked(http.get).mockResolvedValue(mockProjectGroups);

      await projectService.getProjects(2, 50, "group-1");

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GETS}?page=2&pageSize=50&tenantGroupId=group-1`,
        undefined,
        { absoluteUrl: true },
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(projectService.getProjects()).rejects.toThrow("Network error");
    });
  });

  describe("getProject", () => {
    it("should GET a single project by id as an absolute URL", async () => {
      vi.mocked(http.get).mockResolvedValue(mockProjectResponse);

      const result = await projectService.getProject({ projectId: "project-1" });

      expect(http.get).toHaveBeenCalledWith(
        `${PROJECT_ENDPOINTS.GET}?projectId=project-1`,
        undefined,
        { absoluteUrl: true },
      );
      expect(result).toEqual(mockProjectResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(projectService.getProject({ projectId: "project-1" })).rejects.toThrow(
        "Network error",
      );
    });
  });
});
