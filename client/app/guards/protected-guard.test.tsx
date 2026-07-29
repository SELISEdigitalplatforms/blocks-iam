import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  appState: { isMounted: true },
  meResult: { data: undefined as unknown, userFound: false },
  authStore: { setUser: vi.fn() },
  statusChecker: {
    data: undefined as unknown,
    isLoading: false,
    isSuccess: true,
    isError: false,
  },
  startImpersonation: { mutateAsync: vi.fn().mockResolvedValue(undefined) },
  stopImpersonation: { mutateAsync: vi.fn().mockResolvedValue(undefined) },
  impersonateStore: {
    isImpersonated: false,
    impersonatedTenantId: null as string | null,
    isInitialized: true,
    setImpersonation: vi.fn(),
    setInitialized: vi.fn(),
    impersonate: vi.fn(),
    terminate: vi.fn(),
  },
  projectStore: { selectedProject: null as null | { tenantId: string } },
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("./public-guard", () => ({ useAppState: vi.fn(() => h.appState) }));
vi.mock("@/idp/iam/hooks/use-user", () => ({ useGetMe: vi.fn(() => h.meResult) }));
vi.mock("@/store/useAuthStore", () => ({ useAuthStore: vi.fn(() => h.authStore) }));
vi.mock("@/store/useProjectStore", () => ({ useProjectStore: vi.fn(() => h.projectStore) }));
vi.mock("@/store/impersonate-store", () => ({
  useImpersonateStore: vi.fn(() => h.impersonateStore),
}));
vi.mock("@/hooks/use-impersonation", () => ({
  useImpersonationStatusChecker: vi.fn(() => h.statusChecker),
  useStartImpersonation: vi.fn(() => h.startImpersonation),
  useStopImpersonation: vi.fn(() => h.stopImpersonation),
}));
vi.mock("@/components/loader-spinner/loader-spinner", () => ({
  default: () => <div>loading-spinner</div>,
}));

import {
  ProtectedGuard,
  ImpersonationChecker,
  ImpersonationTerminator,
  ImpersonationSynchronizer,
} from "./protected-guard";

beforeEach(() => {
  vi.clearAllMocks();
  h.appState.isMounted = true;
  h.meResult = { data: undefined, userFound: false };
  h.statusChecker = { data: undefined, isLoading: false, isSuccess: true, isError: false };
  h.impersonateStore.isImpersonated = false;
  h.impersonateStore.impersonatedTenantId = null;
  h.impersonateStore.isInitialized = true;
  h.projectStore.selectedProject = null;
});

describe("ProtectedGuard", () => {
  it("renders children when mounted with an authenticated user", async () => {
    h.meResult = { data: { data: { id: "u1" } }, userFound: true };
    render(<ProtectedGuard><div>protected-child</div></ProtectedGuard>);
    await waitFor(() => expect(screen.getByText("protected-child")).toBeInTheDocument());
    expect(h.navigateMock).not.toHaveBeenCalled();
  });

  it("redirects to /login when the user is not found", async () => {
    h.meResult = { data: undefined, userFound: false };
    render(<ProtectedGuard><div>protected-child</div></ProtectedGuard>);
    await waitFor(() =>
      expect(h.navigateMock).toHaveBeenCalledWith("/login", { replace: true }),
    );
    expect(screen.queryByText("protected-child")).not.toBeInTheDocument();
  });

  it("renders nothing before the app is mounted", () => {
    h.appState.isMounted = false;
    h.meResult = { data: { data: {} }, userFound: true };
    render(<ProtectedGuard><div>protected-child</div></ProtectedGuard>);
    expect(screen.queryByText("protected-child")).not.toBeInTheDocument();
  });
});

describe("ImpersonationChecker", () => {
  it("shows a spinner while the status is loading", () => {
    h.statusChecker = { data: undefined, isLoading: true, isSuccess: false, isError: false };
    render(<ImpersonationChecker><div>checked-child</div></ImpersonationChecker>);
    expect(screen.getByText("loading-spinner")).toBeInTheDocument();
  });

  it("stores impersonation state and renders children once resolved", async () => {
    h.statusChecker = {
      data: {
        impersonated: true,
        originalTenantId: "orig",
        impersonatedTenantId: "imp",
      },
      isLoading: false,
      isSuccess: true,
      isError: false,
    };
    render(<ImpersonationChecker><div>checked-child</div></ImpersonationChecker>);
    await waitFor(() =>
      expect(h.impersonateStore.setImpersonation).toHaveBeenCalledWith(true, "orig", "imp"),
    );
    expect(h.impersonateStore.setInitialized).toHaveBeenCalledWith(true);
    expect(screen.getByText("checked-child")).toBeInTheDocument();
  });
});

describe("ImpersonationTerminator", () => {
  it("renders children when there is nothing to terminate", () => {
    h.impersonateStore.isImpersonated = false;
    render(<ImpersonationTerminator><div>term-child</div></ImpersonationTerminator>);
    expect(screen.getByText("term-child")).toBeInTheDocument();
  });

  it("triggers stopImpersonation when impersonated", async () => {
    h.impersonateStore.isImpersonated = true;
    render(<ImpersonationTerminator><div>term-child</div></ImpersonationTerminator>);
    await waitFor(() => expect(h.stopImpersonation.mutateAsync).toHaveBeenCalled());
  });
});

describe("ImpersonationSynchronizer", () => {
  it("renders nothing when not impersonated and no project selected", () => {
    h.impersonateStore.isImpersonated = false;
    h.projectStore.selectedProject = null;
    render(<ImpersonationSynchronizer><div>sync-child</div></ImpersonationSynchronizer>);
    expect(screen.queryByText("sync-child")).not.toBeInTheDocument();
  });

  it("starts impersonation when a new project tenant is selected", async () => {
    h.projectStore.selectedProject = { tenantId: "new-tenant" };
    h.impersonateStore.impersonatedTenantId = "old-tenant";
    render(<ImpersonationSynchronizer><div>sync-child</div></ImpersonationSynchronizer>);
    await waitFor(() =>
      expect(h.startImpersonation.mutateAsync).toHaveBeenCalledWith({
        targeted_tenant_id: "new-tenant",
      }),
    );
  });
});
