import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockProjectStoreFactory, TEST_TENANT_ID } from "@/test-utils/__mocks__";
import { githubInfoService } from "../services/github-info.service";
import {
  useGithubVerification,
  useValidateAuthorization,
  useRevokeAccess,
  useGetGithubRepos,
  useGetRepositoryUser,
  useRemoveAuthorization,
  useGithubBranches,
  useRepoAndGitBranchMatch,
  useGetAllProjects,
  useGetAllRepoBuilds,
  useGetRepoDetails,
  useInitialRepoDeployment,
  useManualDeployment,
  useGetSpecs,
  useGetCardProjectAndBranch,
  useChangeBuildSpecs,
  useChangeRepoSpecs,
} from "./github-info";

vi.mock("../services/github-info.service", () => ({
  githubInfoService: {
    verifyAuthorization: vi.fn(),
    checkAlreadyAuthorization: vi.fn(),
    revokeAccess: vi.fn(),
    removeAuthorization: vi.fn(),
    getGithubRepos: vi.fn(),
    getRepositoryUser: vi.fn(),
    getGithubBranches: vi.fn(),
    getRepoAndGitBranchMatch: vi.fn(),
    cloneGithubRepo: vi.fn(),
    repoInitialDeploy: vi.fn(),
    manualDeploy: vi.fn(),
    getSpecs: vi.fn(),
    getAllRepos: vi.fn(),
    getAllRepoBuilds: vi.fn(),
    getAllProjects: vi.fn(),
    getRepoDetails: vi.fn(),
    getCardRepoAndBranches: vi.fn(),
    changeBuildSpecs: vi.fn(),
    changeRepoSpecs: vi.fn(),
    changeRepoSettings: vi.fn(),
    getBuildLogs: vi.fn(),
    getRepoCardsAndBranches: vi.fn(),
  },
}));

vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

// ─── Inline mock data ─────────────────────────────────────────────────────────
const mockToken = "access-token-value";
const mockAuthStatus = { isSuccess: true };
const mockRepos = { data: { items: [{ id: 1 }], total_count: 1 }, isSuccess: true };
const mockUser = { id: "user-1", login: "octocat" };
const mockBranches = [{ name: "main" }];
const mockBranchMatch = { isMatch: true };
const mockProjects = { items: [{ projectId: "p-1" }] };
const mockRepoBuilds = [{ repoId: "r-1" }];
const mockRepoDetails = { repoId: "r-1", name: "repo-1" };
const mockSpecs = { cpu: "2" };
const mockBuild = { buildId: "b-1" };
const mockMutationResult = { isSuccess: true };

describe("Github Info Hooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ─── useGithubVerification ──────────────────────────────────────────────────
  describe("useGithubVerification", () => {
    it("should verify authorization successfully when code is provided", async () => {
      vi.mocked(githubInfoService.verifyAuthorization).mockResolvedValue(mockToken);

      const { result } = renderHook(() => useGithubVerification("auth-code"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toBe(mockToken);
      expect(githubInfoService.verifyAuthorization).toHaveBeenCalledWith(
        "auth-code",
        TEST_TENANT_ID,
      );
    });

    it("should stay idle and not call the service when code is empty", async () => {
      vi.mocked(githubInfoService.verifyAuthorization).mockResolvedValue(mockToken);

      const { result } = renderHook(() => useGithubVerification(""), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.verifyAuthorization).not.toHaveBeenCalled();
    });

    it("should handle errors", async () => {
      vi.mocked(githubInfoService.verifyAuthorization).mockRejectedValue(new Error("failed"));

      const { result } = renderHook(() => useGithubVerification("auth-code"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useValidateAuthorization ───────────────────────────────────────────────
  describe("useValidateAuthorization", () => {
    it("should validate authorization successfully", async () => {
      vi.mocked(githubInfoService.checkAlreadyAuthorization).mockResolvedValue(mockAuthStatus);

      const { result } = renderHook(() => useValidateAuthorization(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockAuthStatus);
      expect(githubInfoService.checkAlreadyAuthorization).toHaveBeenCalled();
    });

    it("should handle errors", async () => {
      vi.mocked(githubInfoService.checkAlreadyAuthorization).mockRejectedValue(
        new Error("failed"),
      );

      const { result } = renderHook(() => useValidateAuthorization(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useRevokeAccess ────────────────────────────────────────────────────────
  describe("useRevokeAccess", () => {
    it("should be disabled and not call the service on mount", async () => {
      vi.mocked(githubInfoService.revokeAccess).mockResolvedValue(mockAuthStatus);

      const { result } = renderHook(() => useRevokeAccess(), { wrapper: createWrapper() });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.revokeAccess).not.toHaveBeenCalled();
    });
  });

  // ─── useGetGithubRepos ──────────────────────────────────────────────────────
  describe("useGetGithubRepos", () => {
    it("should fetch repos when verification is successful", async () => {
      vi.mocked(githubInfoService.getGithubRepos).mockResolvedValue(mockRepos);

      const { result } = renderHook(() => useGetGithubRepos(true, "search", 1, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockRepos);
      expect(githubInfoService.getGithubRepos).toHaveBeenCalledWith(
        TEST_TENANT_ID,
        "search",
        1,
        10,
      );
    });

    it("should be disabled when verification is not successful", async () => {
      vi.mocked(githubInfoService.getGithubRepos).mockResolvedValue(mockRepos);

      const { result } = renderHook(() => useGetGithubRepos(false), { wrapper: createWrapper() });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getGithubRepos).not.toHaveBeenCalled();
    });
  });

  // ─── useGetRepositoryUser ───────────────────────────────────────────────────
  describe("useGetRepositoryUser", () => {
    it("should fetch the repository user when verification is successful", async () => {
      vi.mocked(githubInfoService.getRepositoryUser).mockResolvedValue(mockUser);

      const { result } = renderHook(() => useGetRepositoryUser(true), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockUser);
      expect(githubInfoService.getRepositoryUser).toHaveBeenCalledWith(TEST_TENANT_ID);
    });

    it("should be disabled when verification is not successful", async () => {
      vi.mocked(githubInfoService.getRepositoryUser).mockResolvedValue(mockUser);

      const { result } = renderHook(() => useGetRepositoryUser(false), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getRepositoryUser).not.toHaveBeenCalled();
    });
  });

  // ─── useRemoveAuthorization ─────────────────────────────────────────────────
  describe("useRemoveAuthorization", () => {
    it("should remove authorization successfully", async () => {
      vi.mocked(githubInfoService.removeAuthorization).mockResolvedValue(mockAuthStatus);

      const { result } = renderHook(() => useRemoveAuthorization(), {
        wrapper: createWrapper(),
      });

      result.current.mutate();

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(githubInfoService.removeAuthorization).toHaveBeenCalled();
    });

    it("should handle errors", async () => {
      vi.mocked(githubInfoService.removeAuthorization).mockRejectedValue(new Error("failed"));

      const { result } = renderHook(() => useRemoveAuthorization(), {
        wrapper: createWrapper(),
      });

      result.current.mutate();

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGithubBranches ──────────────────────────────────────────────────────
  describe("useGithubBranches", () => {
    it("should fetch branches when a repo is provided", async () => {
      vi.mocked(githubInfoService.getGithubBranches).mockResolvedValue(mockBranches);

      const { result } = renderHook(() => useGithubBranches("owner/repo"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockBranches);
      expect(githubInfoService.getGithubBranches).toHaveBeenCalledWith(
        "owner/repo",
        TEST_TENANT_ID,
      );
    });

    it("should be disabled when repo is empty", async () => {
      vi.mocked(githubInfoService.getGithubBranches).mockResolvedValue(mockBranches);

      const { result } = renderHook(() => useGithubBranches(""), { wrapper: createWrapper() });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getGithubBranches).not.toHaveBeenCalled();
    });
  });

  // ─── useRepoAndGitBranchMatch ───────────────────────────────────────────────
  describe("useRepoAndGitBranchMatch", () => {
    it("should fetch a branch match when repoId is provided and enabled", async () => {
      vi.mocked(githubInfoService.getRepoAndGitBranchMatch).mockResolvedValue(mockBranchMatch);

      const { result } = renderHook(() => useRepoAndGitBranchMatch("repo-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockBranchMatch);
      expect(githubInfoService.getRepoAndGitBranchMatch).toHaveBeenCalledWith(
        "repo-1",
        TEST_TENANT_ID,
      );
    });

    it("should be disabled when the enabled flag is false", async () => {
      vi.mocked(githubInfoService.getRepoAndGitBranchMatch).mockResolvedValue(mockBranchMatch);

      const { result } = renderHook(() => useRepoAndGitBranchMatch("repo-1", false), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getRepoAndGitBranchMatch).not.toHaveBeenCalled();
    });
  });

  // ─── useGetAllProjects ──────────────────────────────────────────────────────
  describe("useGetAllProjects", () => {
    it("should fetch all projects when a projectId is provided", async () => {
      vi.mocked(githubInfoService.getAllProjects).mockResolvedValue(mockProjects);

      const { result } = renderHook(() => useGetAllProjects("proj-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockProjects);
      expect(githubInfoService.getAllProjects).toHaveBeenCalledWith("proj-1");
    });

    it("should be disabled when projectId is empty", async () => {
      vi.mocked(githubInfoService.getAllProjects).mockResolvedValue(mockProjects);

      const { result } = renderHook(() => useGetAllProjects(""), { wrapper: createWrapper() });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getAllProjects).not.toHaveBeenCalled();
    });
  });

  // ─── useGetAllRepoBuilds ────────────────────────────────────────────────────
  describe("useGetAllRepoBuilds", () => {
    it("should fetch repo builds when a projectId is provided", async () => {
      vi.mocked(githubInfoService.getAllRepoBuilds).mockResolvedValue(mockRepoBuilds);

      const { result } = renderHook(() => useGetAllRepoBuilds("proj-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockRepoBuilds);
      expect(githubInfoService.getAllRepoBuilds).toHaveBeenCalledWith("proj-1");
    });

    it("should be disabled when projectId is empty", async () => {
      vi.mocked(githubInfoService.getAllRepoBuilds).mockResolvedValue(mockRepoBuilds);

      const { result } = renderHook(() => useGetAllRepoBuilds(""), { wrapper: createWrapper() });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getAllRepoBuilds).not.toHaveBeenCalled();
    });
  });

  // ─── useGetRepoDetails ──────────────────────────────────────────────────────
  describe("useGetRepoDetails", () => {
    it("should fetch repo details when projectKey and repoId are provided", async () => {
      vi.mocked(githubInfoService.getRepoDetails).mockResolvedValue(mockRepoDetails);

      const { result } = renderHook(() => useGetRepoDetails("proj-1", "repo-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockRepoDetails);
      expect(githubInfoService.getRepoDetails).toHaveBeenCalledWith("proj-1", "repo-1");
    });

    it("should be disabled when repoId is empty", async () => {
      vi.mocked(githubInfoService.getRepoDetails).mockResolvedValue(mockRepoDetails);

      const { result } = renderHook(() => useGetRepoDetails("proj-1", ""), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getRepoDetails).not.toHaveBeenCalled();
    });
  });

  // ─── useInitialRepoDeployment ───────────────────────────────────────────────
  describe("useInitialRepoDeployment", () => {
    it("should trigger the initial deployment mutation successfully", async () => {
      vi.mocked(githubInfoService.repoInitialDeploy).mockResolvedValue(mockMutationResult);

      const { result } = renderHook(() => useInitialRepoDeployment(), {
        wrapper: createWrapper(),
      });

      const payload = { repoId: "r-1", branch: "main" };
      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(githubInfoService.repoInitialDeploy).toHaveBeenCalledWith(payload);
    });

    it("should handle errors", async () => {
      vi.mocked(githubInfoService.repoInitialDeploy).mockRejectedValue(new Error("failed"));

      const { result } = renderHook(() => useInitialRepoDeployment(), {
        wrapper: createWrapper(),
      });

      result.current.mutate({ repoId: "r-1" } as never);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useManualDeployment ────────────────────────────────────────────────────
  describe("useManualDeployment", () => {
    it("should trigger the manual deployment mutation successfully", async () => {
      vi.mocked(githubInfoService.manualDeploy).mockResolvedValue(mockMutationResult);

      const { result } = renderHook(() => useManualDeployment(), {
        wrapper: createWrapper(),
      });

      const payload = { repoId: "r-1", commitId: "abc123" };
      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(githubInfoService.manualDeploy).toHaveBeenCalledWith(payload);
    });
  });

  // ─── useGetSpecs ────────────────────────────────────────────────────────────
  describe("useGetSpecs", () => {
    it("should fetch build specs successfully", async () => {
      vi.mocked(githubInfoService.getSpecs).mockResolvedValue(mockSpecs);

      const { result } = renderHook(() => useGetSpecs(), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockSpecs);
      expect(githubInfoService.getSpecs).toHaveBeenCalled();
    });
  });

  // ─── useGetCardProjectAndBranch ─────────────────────────────────────────────
  describe("useGetCardProjectAndBranch", () => {
    it("should fetch card project and branch when buildId is provided", async () => {
      vi.mocked(githubInfoService.getCardRepoAndBranches).mockResolvedValue(mockBuild);

      const { result } = renderHook(() => useGetCardProjectAndBranch("build-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockBuild);
      expect(githubInfoService.getCardRepoAndBranches).toHaveBeenCalledWith(
        "build-1",
        TEST_TENANT_ID,
      );
    });

    it("should be disabled when buildId is empty", async () => {
      vi.mocked(githubInfoService.getCardRepoAndBranches).mockResolvedValue(mockBuild);

      const { result } = renderHook(() => useGetCardProjectAndBranch(""), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(githubInfoService.getCardRepoAndBranches).not.toHaveBeenCalled();
    });
  });

  // ─── useChangeBuildSpecs ────────────────────────────────────────────────────
  describe("useChangeBuildSpecs", () => {
    it("should change build specs successfully", async () => {
      vi.mocked(githubInfoService.changeBuildSpecs).mockResolvedValue(mockMutationResult);

      const { result } = renderHook(() => useChangeBuildSpecs(), {
        wrapper: createWrapper(),
      });

      const payload = { buildId: "b-1", cpu: "4" };
      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(githubInfoService.changeBuildSpecs).toHaveBeenCalledWith(payload);
    });

    it("should handle errors", async () => {
      vi.mocked(githubInfoService.changeBuildSpecs).mockRejectedValue(new Error("failed"));

      const { result } = renderHook(() => useChangeBuildSpecs(), {
        wrapper: createWrapper(),
      });

      result.current.mutate({ buildId: "b-1" } as never);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useChangeRepoSpecs ─────────────────────────────────────────────────────
  describe("useChangeRepoSpecs", () => {
    it("should change repo specs successfully", async () => {
      vi.mocked(githubInfoService.changeRepoSpecs).mockResolvedValue(mockMutationResult);

      const { result } = renderHook(() => useChangeRepoSpecs(), {
        wrapper: createWrapper(),
      });

      const payload = { repoId: "r-1", cpu: "2" };
      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(githubInfoService.changeRepoSpecs).toHaveBeenCalledWith(payload);
    });
  });
});
