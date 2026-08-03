import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const {
  navigateMock,
  localProjectService,
  crossProjectService,
  storeState,
  showSuccessToast,
  showErrorToast,
  resetFormData,
  formState,
} = vi.hoisted(() => {
  const resetFormDataFn = vi.fn();
  return {
    navigateMock: vi.fn(),
    localProjectService: { getProjects: vi.fn(), getProject: vi.fn() },
    crossProjectService: {
      getProjects: vi.fn(),
      getProject: vi.fn(),
      getAssets: vi.fn(),
      addAssets: vi.fn(),
      getEnvRepositories: vi.fn(),
      repoUpdate: vi.fn(),
      updateTenantGroup: vi.fn(),
      validateCNameProject: vi.fn(),
      disableProject: vi.fn(),
      createProject: vi.fn(),
      getMigrationStatus: vi.fn(),
      initiateMigration: vi.fn(),
      verifyMigration: vi.fn(),
    },
    storeState: {
      selectedProject: { itemId: "proj-1", tenantId: "tenant-1" },
      setProjects: vi.fn(),
      setSelectedProject: vi.fn(),
      setTenantGroup: vi.fn(),
    },
    showSuccessToast: vi.fn(),
    showErrorToast: vi.fn(),
    resetFormData: resetFormDataFn,
    formState: {
      formData: [
        { name: "My Project", isAcceptBlocksTerms: true, isUseBlocksExclusively: false },
        { assets: [{ full_name: "org/repo", html_url: "https://gh/org/repo", id: 42 }] },
        { environments: [{ value: "main" }, { value: "dev" }] },
      ],
      resetFormData: resetFormDataFn,
    },
  };
});

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => navigateMock };
});
vi.mock("@/services/project.service", () => ({ projectService: localProjectService }));
vi.mock("@blocks-identifier/services/project.service", () => ({
  projectService: crossProjectService,
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => storeState),
}));
vi.mock("@/hooks/use-toast", () => ({ showSuccessToast, showErrorToast }));
vi.mock("@/components/create-project/utils", () => ({
  useCreateProjectFormState: vi.fn(() => formState),
  shortGuidGenerator: vi.fn(() => "abcde"),
}));

import {
  useGetProjects,
  useGetProject,
  useGetAssets,
  useAddAssets,
  useGetEnvRepositories,
  useUpdateRepositories,
  useUpdateProject,
  useUpdateTenantGroup,
  useValidateCNameProject,
  useDisableProject,
  useCreateProject,
  useGetMigrationStatus,
  useInitiateMigration,
  useVerifyMigration,
  useProjectForm,
} from "./use-project";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("useGetProjects", () => {
  it("fetches project groups and pushes flattened projects to the store", async () => {
    localProjectService.getProjects.mockResolvedValue([
      { projects: [{ itemId: "a" }, { itemId: "b" }] },
      { projects: [{ itemId: "c" }] },
    ]);

    const { result } = renderHook(() => useGetProjects("tg-1"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(localProjectService.getProjects).toHaveBeenCalledWith(0, 100, "tg-1");
    expect(storeState.setProjects).toHaveBeenCalledWith([
      { itemId: "a" },
      { itemId: "b" },
      { itemId: "c" },
    ]);
  });

  it("does not call setProjects when there is no data", async () => {
    localProjectService.getProjects.mockRejectedValue(new Error("fail"));
    const { result } = renderHook(() => useGetProjects(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(storeState.setProjects).not.toHaveBeenCalled();
  });
});

describe("useGetProject", () => {
  it("uses the explicit projectId when provided", async () => {
    localProjectService.getProject.mockResolvedValue({ itemId: "x" });
    const { result } = renderHook(() => useGetProject({ projectId: "explicit" }), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(localProjectService.getProject).toHaveBeenCalledWith({ projectId: "explicit" });
  });

  it("falls back to the selected project id from the store", async () => {
    localProjectService.getProject.mockResolvedValue({ itemId: "proj-1" });
    const { result } = renderHook(() => useGetProject(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(localProjectService.getProject).toHaveBeenCalledWith({ projectId: "proj-1" });
  });
});

describe("query hooks with enabled gating", () => {
  it("useGetAssets calls the cross service", async () => {
    crossProjectService.getAssets.mockResolvedValue({ resources: [] });
    const { result } = renderHook(() => useGetAssets("tg-1"), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.getAssets).toHaveBeenCalledWith("tg-1");
  });

  it("useGetEnvRepositories is disabled without a projectKey", () => {
    const { result } = renderHook(() => useGetEnvRepositories(""), { wrapper: createWrapper() });
    expect(result.current.fetchStatus).toBe("idle");
    expect(crossProjectService.getEnvRepositories).not.toHaveBeenCalled();
  });

  it("useGetMigrationStatus fetches", async () => {
    crossProjectService.getMigrationStatus.mockResolvedValue({ status: "done" });
    const { result } = renderHook(() => useGetMigrationStatus("tg-1"), {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.getMigrationStatus).toHaveBeenCalledWith("tg-1");
  });
});

describe("mutation hooks", () => {
  const cases: Array<[string, () => { mutate: (p: unknown) => void }, unknown, () => unknown]> = [];

  it("useAddAssets mutates via addAssets", async () => {
    crossProjectService.addAssets.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useAddAssets(), { wrapper: createWrapper() });
    result.current.mutate({ tenantGroupId: "tg", resource: {} });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.addAssets).toHaveBeenCalled();
  });

  it("useUpdateRepositories mutates via repoUpdate", async () => {
    crossProjectService.repoUpdate.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useUpdateRepositories(), { wrapper: createWrapper() });
    result.current.mutate({ id: "1" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.repoUpdate).toHaveBeenCalled();
  });

  it("useUpdateProject mutates via updateTenantGroup", async () => {
    crossProjectService.updateTenantGroup.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useUpdateProject({ projectKey: "pk" }), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ name: "n", tenantGroupId: "tg" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.updateTenantGroup).toHaveBeenCalledWith({
      name: "n",
      tenantGroupId: "tg",
    });
  });

  it("useUpdateTenantGroup mutates via updateTenantGroup", async () => {
    crossProjectService.updateTenantGroup.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useUpdateTenantGroup({ tenantGroupId: "tg" }), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ name: "n", tenantGroupId: "tg" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.updateTenantGroup).toHaveBeenCalled();
  });

  it("useValidateCNameProject mutates via validateCNameProject", async () => {
    crossProjectService.validateCNameProject.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useValidateCNameProject({ projectKey: "pk" }), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ projectKey: "pk" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.validateCNameProject).toHaveBeenCalled();
  });

  it("useDisableProject mutates via disableProject", async () => {
    crossProjectService.disableProject.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useDisableProject({ projectKey: "pk" }), {
      wrapper: createWrapper(),
    });
    result.current.mutate({ projectKey: "pk" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.disableProject).toHaveBeenCalled();
  });

  it("useCreateProject mutates via createProject", async () => {
    crossProjectService.createProject.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useCreateProject(), { wrapper: createWrapper() });
    result.current.mutate({ name: "p" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.createProject).toHaveBeenCalled();
  });

  it("useInitiateMigration mutates via initiateMigration", async () => {
    crossProjectService.initiateMigration.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useInitiateMigration(), { wrapper: createWrapper() });
    result.current.mutate({ tenantGroupId: "tg" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.initiateMigration).toHaveBeenCalled();
  });

  it("useVerifyMigration mutates via verifyMigration", async () => {
    crossProjectService.verifyMigration.mockResolvedValue({ isSuccess: true });
    const { result } = renderHook(() => useVerifyMigration(), { wrapper: createWrapper() });
    result.current.mutate({ tenantGroupId: "tg" });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(crossProjectService.verifyMigration).toHaveBeenCalled();
  });

  void cases;
});

describe("useProjectForm.saveProject", () => {
  it("creates the project, selects the first result, toasts and navigates on success", async () => {
    crossProjectService.createProject.mockResolvedValue({
      isSuccess: true,
      tenantGroupId: "tg-new",
      errors: null,
    });
    localProjectService.getProjects.mockResolvedValue([
      { projects: [{ itemId: "created-project" }] },
    ]);

    const { result } = renderHook(() => useProjectForm(), { wrapper: createWrapper() });
    await result.current.saveProject();

    // application contexts built from environments, main => no prefix
    const payload = crossProjectService.createProject.mock.calls[0][0];
    expect(payload.name).toBe("My Project");
    expect(payload.applicationContexts).toHaveLength(2);
    expect(payload.applicationContexts[0].domain).toContain("abcde");
    expect(payload.resources[0]).toEqual({
      name: "org/repo",
      link: "https://gh/org/repo",
      resourceId: "42",
    });

    expect(showSuccessToast).toHaveBeenCalledWith({
      description: "Your project has been created.",
    });
    expect(storeState.setTenantGroup).toHaveBeenCalledWith("tg-new");
    expect(storeState.setSelectedProject).toHaveBeenCalledWith({ itemId: "created-project" });
    expect(navigateMock).toHaveBeenCalledWith("/app/project/tg-new/environments");
    expect(resetFormData).toHaveBeenCalled();
  });

  it("shows an error toast when the create response is not successful", async () => {
    crossProjectService.createProject.mockResolvedValue({
      isSuccess: false,
      errors: { name: "taken" },
    });

    const { result } = renderHook(() => useProjectForm(), { wrapper: createWrapper() });
    await result.current.saveProject();

    expect(showErrorToast).toHaveBeenCalledWith({ errors: { name: "taken" } });
    expect(navigateMock).not.toHaveBeenCalled();
  });

  it("shows an error toast when mutateAsync throws with an errors field", async () => {
    crossProjectService.createProject.mockRejectedValue({ errors: { global: "boom" } });

    const { result } = renderHook(() => useProjectForm(), { wrapper: createWrapper() });
    await result.current.saveProject();

    expect(showErrorToast).toHaveBeenCalledWith({ errors: { global: "boom" } });
  });
});
