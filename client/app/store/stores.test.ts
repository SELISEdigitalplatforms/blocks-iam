import { beforeEach, describe, expect, it } from "vitest";
import { useAuthStore } from "./useAuthStore";
import { useImpersonateStore } from "./impersonate-store";
import { useExecutionContextStore } from "./execution-context-store";
import { useProjectStore } from "./useProjectStore";
import type { User } from "@blocks-idp/iam/models/user";
import type { IProject } from "@blocks-identifier/models/project.model";

const user = { itemId: "u1", email: "a@b.com" } as User;
const project = {
  itemId: "p1",
  tenantId: "t1",
  tenantGroupId: "tg1",
  name: "Proj",
} as unknown as IProject;

describe("useAuthStore", () => {
  beforeEach(() => useAuthStore.getState().reset());

  it("starts unauthenticated with no user or tokens", () => {
    const s = useAuthStore.getState();
    expect(s.isAuthenticated).toBe(false);
    expect(s.user).toBeNull();
    expect(s.accessToken).toBeNull();
  });

  it("sets and authenticates a user", () => {
    useAuthStore.getState().setUser(user);
    useAuthStore.getState().setAuthenticated();
    expect(useAuthStore.getState().user).toEqual(user);
    expect(useAuthStore.getState().isAuthenticated).toBe(true);
  });

  it("clears user and auth flag on setUnAuthenticated", () => {
    useAuthStore.getState().setUser(user);
    useAuthStore.getState().setAuthenticated();
    useAuthStore.getState().setUnAuthenticated();
    expect(useAuthStore.getState().isAuthenticated).toBe(false);
    expect(useAuthStore.getState().user).toBeNull();
  });

  it("sets and clears tokens", () => {
    useAuthStore.getState().setTokens("access", "refresh");
    expect(useAuthStore.getState().accessToken).toBe("access");
    expect(useAuthStore.getState().refreshToken).toBe("refresh");
    useAuthStore.getState().clearTokens();
    expect(useAuthStore.getState().accessToken).toBeNull();
    expect(useAuthStore.getState().refreshToken).toBeNull();
  });

  it("reset restores initial state", () => {
    useAuthStore.getState().setTokens("a", "b");
    useAuthStore.getState().setAuthenticated();
    useAuthStore.getState().reset();
    const s = useAuthStore.getState();
    expect(s.isAuthenticated).toBe(false);
    expect(s.accessToken).toBeNull();
  });
});

describe("useImpersonateStore", () => {
  beforeEach(() => useImpersonateStore.getState().reset());

  it("impersonate() sets the impersonation state", () => {
    useImpersonateStore.getState().impersonate("imp-tenant", "orig-tenant");
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(true);
    expect(s.impersonatedTenantId).toBe("imp-tenant");
    expect(s.originalTenantId).toBe("orig-tenant");
  });

  it("terminate() clears impersonation but keeps the original tenant", () => {
    useImpersonateStore.getState().impersonate("imp-tenant", "orig-tenant");
    useImpersonateStore.getState().terminate("orig-tenant");
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(false);
    expect(s.impersonatedTenantId).toBeNull();
    expect(s.originalTenantId).toBe("orig-tenant");
  });

  it("setImpersonation() sets all three fields explicitly", () => {
    useImpersonateStore.getState().setImpersonation(true, "orig", "imp");
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(true);
    expect(s.originalTenantId).toBe("orig");
    expect(s.impersonatedTenantId).toBe("imp");
  });

  it("setInitialized() toggles the initialized flag", () => {
    useImpersonateStore.getState().setInitialized(true);
    expect(useImpersonateStore.getState().isInitialized).toBe(true);
  });

  it("reset() restores defaults", () => {
    useImpersonateStore.getState().impersonate("i", "o");
    useImpersonateStore.getState().setInitialized(true);
    useImpersonateStore.getState().reset();
    const s = useImpersonateStore.getState();
    expect(s.isImpersonated).toBe(false);
    expect(s.isInitialized).toBe(false);
  });
});

describe("useExecutionContextStore", () => {
  beforeEach(() => useExecutionContextStore.getState().reset());

  it("setContext / resetContext manage the context", () => {
    const ctx = { tenantId: "t1", contextId: "c1" };
    useExecutionContextStore.getState().setContext(ctx);
    expect(useExecutionContextStore.getState().context).toEqual(ctx);
    useExecutionContextStore.getState().resetContext();
    expect(useExecutionContextStore.getState().context).toBeNull();
  });
});

describe("useProjectStore", () => {
  beforeEach(() => useProjectStore.getState().reset());

  it("setSelectedProject also derives the tenant group", () => {
    useProjectStore.getState().setSelectedProject(project);
    const s = useProjectStore.getState();
    expect(s.selectedProject).toEqual(project);
    expect(s.selectedTenantGroup).toBe("tg1");
  });

  it("setProjects / resetProject manage the project list", () => {
    useProjectStore.getState().setProjects([project]);
    expect(useProjectStore.getState().projects).toHaveLength(1);
    useProjectStore.getState().resetProject();
    expect(useProjectStore.getState().projects).toHaveLength(0);
  });

  it("resetSelectedProject clears the selected project", () => {
    useProjectStore.getState().setSelectedProject(project);
    useProjectStore.getState().resetSelectedProject();
    expect(useProjectStore.getState().selectedProject).toBeNull();
  });

  it("tenant-group setters manage the selected tenant group", () => {
    useProjectStore.getState().setTennantGroup("tg-42");
    expect(useProjectStore.getState().selectedTenantGroup).toBe("tg-42");
    useProjectStore.getState().resetTennantGroup();
    expect(useProjectStore.getState().selectedTenantGroup).toBeNull();
  });
});
