import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { GithubInfoService } from "./github-info.service";
import { CLOUD_BUILD_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

// ─── Inline mock data ─────────────────────────────────────────────────────────
const PROJECT_KEY = "test-project-key-123";
const RELEASE_BASE_URL = "https://release.example.com";

const mockSuccessResponse = { isSuccess: true };
const mockReposResponse = {
  data: { items: [{ id: 1, name: "repo-1" }], total_count: 1 },
  message: null,
  statusCode: 200,
  errors: null,
  isSuccess: true,
};
const mockUserResponse = { id: "user-1", login: "octocat" };
const mockBranches = [{ name: "main" }, { name: "dev" }];
const mockBranchMatch = { isMatch: true, branch: "main" };
const mockSettings = { cpu: "2", memory: "4Gi" };
const mockAllRepos = [{ repoId: "r-1", branches: ["main"] }];
const mockRepoDetails = { repoId: "r-1", name: "repo-1" };
const mockBuildResponse = { buildId: "b-1", logs: [] };
const mockProjectsList = { items: [{ projectId: "p-1" }] };
const mockClonePayload = { repoUrl: "https://github.com/x/y.git", branch: "main" };
const mockDeployPayload = { repoId: "r-1", branch: "main" };
const mockManualPayload = { repoId: "r-1", commitId: "abc123" };
const mockChangeSettingsPayload = { buildId: "b-1", cpu: "4" };
const mockChangeRepoSpecsPayload = { repoId: "r-1", cpu: "2" };

describe("GithubInfoService", () => {
  let service: GithubInfoService;

  beforeEach(() => {
    service = new GithubInfoService();
    (window as unknown as { __BLOCKS_ENV__: Record<string, string> }).__BLOCKS_ENV__ = {
      ...(window as unknown as { __BLOCKS_ENV__: Record<string, string> }).__BLOCKS_ENV__,
      BLOCKS_RELEASE_BASE_URL: RELEASE_BASE_URL,
    };
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── verifyAuthorization ────────────────────────────────────────────────────
  describe("verifyAuthorization", () => {
    it("should GET with encoded code and project key", async () => {
      vi.mocked(http.get).mockResolvedValue("access-token-value");

      const result = await service.verifyAuthorization("abc/def+ghi", PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.ACCESS_TOKEN}?code=abc%2Fdef%2Bghi&ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toBe("access-token-value");
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.verifyAuthorization("code", PROJECT_KEY)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── checkAlreadyAuthorization ──────────────────────────────────────────────
  describe("checkAlreadyAuthorization", () => {
    it("should GET the isAuthorized endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSuccessResponse);

      const result = await service.checkAlreadyAuthorization();

      expect(http.get).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.IS_AUTHORIZED);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.checkAlreadyAuthorization()).rejects.toThrow("Network error");
    });
  });

  // ─── revokeAccess ───────────────────────────────────────────────────────────
  describe("revokeAccess", () => {
    it("should POST to removeAuthorization with empty body", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.revokeAccess();

      expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.REMOVE_AUTHORIZATION, {});
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.revokeAccess()).rejects.toThrow("Network error");
    });
  });

  // ─── removeAuthorization ────────────────────────────────────────────────────
  describe("removeAuthorization", () => {
    it("should POST to removeAccessToken with empty body", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.removeAuthorization();

      expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.REMOVE_ACCESS_TOKEN, {});
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── getGithubRepos ─────────────────────────────────────────────────────────
  describe("getGithubRepos", () => {
    it("should GET with only the project key when no optional params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockReposResponse);

      const result = await service.getGithubRepos(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.GITHUB_REPOS}?ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockReposResponse);
    });

    it("should GET with encoded search and pagination params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockReposResponse);

      await service.getGithubRepos(PROJECT_KEY, "my repo", 2, 10);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.GITHUB_REPOS}?ProjectKey=${PROJECT_KEY}&search=my%20repo&pageNumber=2&pageSize=10`,
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getGithubRepos(PROJECT_KEY)).rejects.toThrow("Network error");
    });
  });

  // ─── getRepositoryUser ──────────────────────────────────────────────────────
  describe("getRepositoryUser", () => {
    it("should GET the github user endpoint with project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockUserResponse);

      const result = await service.getRepositoryUser(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.GITHUB_USER}?ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockUserResponse);
    });
  });

  // ─── getGithubBranches ──────────────────────────────────────────────────────
  describe("getGithubBranches", () => {
    it("should GET with encoded repo and project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockBranches);

      const result = await service.getGithubBranches("owner/repo", PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.GITHUB_BRANCHES}?repo=owner%2Frepo&ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockBranches);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getGithubBranches("repo", PROJECT_KEY)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── getRepoAndGitBranchMatch ───────────────────────────────────────────────
  describe("getRepoAndGitBranchMatch", () => {
    it("should GET the branchExists endpoint with encoded params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockBranchMatch);

      const result = await service.getRepoAndGitBranchMatch("repo id", PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.GITHUB_BRANCH_EXISTS}?repoId=repo%20id&ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockBranchMatch);
    });
  });

  // ─── cloneGithubRepo ────────────────────────────────────────────────────────
  describe("cloneGithubRepo", () => {
    it("should POST to the clone endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.cloneGithubRepo(mockClonePayload as never);

      expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.BUILD_BUILD, mockClonePayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.cloneGithubRepo(mockClonePayload as never)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── repoInitialDeploy ──────────────────────────────────────────────────────
  describe("repoInitialDeploy", () => {
    it("should POST to the run endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.repoInitialDeploy(mockDeployPayload);

      expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.RUN_BUILD, mockDeployPayload);
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── manualDeploy ───────────────────────────────────────────────────────────
  describe("manualDeploy", () => {
    it("should POST to the manual endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.manualDeploy(mockManualPayload as never);

      expect(http.post).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.MANUAL, mockManualPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.manualDeploy(mockManualPayload as never)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── getSpecs ───────────────────────────────────────────────────────────────
  describe("getSpecs", () => {
    it("should GET the settings endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSettings);

      const result = await service.getSpecs();

      expect(http.get).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.SETTINGS);
      expect(result).toEqual(mockSettings);
    });
  });

  // ─── getAllRepos ────────────────────────────────────────────────────────────
  describe("getAllRepos", () => {
    it("should GET the repos endpoint with project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockAllRepos);

      const result = await service.getAllRepos(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.REPOS}?ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockAllRepos);
    });
  });

  // ─── getAllRepoBuilds ───────────────────────────────────────────────────────
  describe("getAllRepoBuilds", () => {
    it("should GET the repos endpoint with project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockAllRepos);

      const result = await service.getAllRepoBuilds(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.REPOS}?ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockAllRepos);
    });
  });

  // ─── getAllProjects ─────────────────────────────────────────────────────────
  describe("getAllProjects", () => {
    it("should GET the absolute release URL with absoluteUrl option", async () => {
      vi.mocked(http.get).mockResolvedValue(mockProjectsList);

      const result = await service.getAllProjects(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${RELEASE_BASE_URL}${CLOUD_BUILD_ENDPOINTS.REPOS_LIST}?ProjectKey=${PROJECT_KEY}`,
        undefined,
        { absoluteUrl: true },
      );
      expect(result).toEqual(mockProjectsList);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getAllProjects(PROJECT_KEY)).rejects.toThrow("Network error");
    });
  });

  // ─── getRepoDetails ─────────────────────────────────────────────────────────
  describe("getRepoDetails", () => {
    it("should GET the repo details endpoint with encoded params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockRepoDetails);

      const result = await service.getRepoDetails(PROJECT_KEY, "repo/1");

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.REPO_DETAILS}?ProjectKey=${PROJECT_KEY}&RepoId=repo%2F1`,
      );
      expect(result).toEqual(mockRepoDetails);
    });
  });

  // ─── getCardRepoAndBranches ─────────────────────────────────────────────────
  describe("getCardRepoAndBranches", () => {
    it("should GET the build endpoint with encoded buildId and project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockBuildResponse);

      const result = await service.getCardRepoAndBranches("build 1", PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.BUILD}?buildId=build%201&ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockBuildResponse);
    });
  });

  // ─── changeBuildSpecs ───────────────────────────────────────────────────────
  describe("changeBuildSpecs", () => {
    it("should PUT to the build endpoint with payload", async () => {
      vi.mocked(http.put).mockResolvedValue(mockSuccessResponse);

      const result = await service.changeBuildSpecs(mockChangeSettingsPayload as never);

      expect(http.put).toHaveBeenCalledWith(CLOUD_BUILD_ENDPOINTS.BUILD, mockChangeSettingsPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.put).mockRejectedValue(new Error("Network error"));

      await expect(service.changeBuildSpecs(mockChangeSettingsPayload as never)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── changeRepoSpecs ────────────────────────────────────────────────────────
  describe("changeRepoSpecs", () => {
    it("should POST to the settings endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.changeRepoSpecs(mockChangeRepoSpecsPayload as never);

      expect(http.post).toHaveBeenCalledWith(
        CLOUD_BUILD_ENDPOINTS.SETTINGS,
        mockChangeRepoSpecsPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── changeRepoSettings ─────────────────────────────────────────────────────
  describe("changeRepoSettings", () => {
    it("should PUT to the settings endpoint with payload", async () => {
      vi.mocked(http.put).mockResolvedValue(mockSuccessResponse);

      const result = await service.changeRepoSettings(mockChangeSettingsPayload as never);

      expect(http.put).toHaveBeenCalledWith(
        CLOUD_BUILD_ENDPOINTS.SETTINGS,
        mockChangeSettingsPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });
  });

  // ─── getBuildLogs ───────────────────────────────────────────────────────────
  describe("getBuildLogs", () => {
    it("should GET the run endpoint with unencoded repoId and encoded project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockBuildResponse);

      const result = await service.getBuildLogs("repo-1", PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.RUN_BUILD}?repoId=repo-1&ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockBuildResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getBuildLogs("repo-1", PROJECT_KEY)).rejects.toThrow("Network error");
    });
  });

  // ─── getRepoCardsAndBranches ────────────────────────────────────────────────
  describe("getRepoCardsAndBranches", () => {
    it("should GET the github repos endpoint with project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockAllRepos);

      const result = await service.getRepoCardsAndBranches(PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${CLOUD_BUILD_ENDPOINTS.GITHUB_REPOS}?ProjectKey=${PROJECT_KEY}`,
      );
      expect(result).toEqual(mockAllRepos);
    });
  });
});
