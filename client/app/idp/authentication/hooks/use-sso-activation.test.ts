import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import { useSsoActivation } from "./use-sso-activation";

const mockPush = vi.fn();
const mockReplace = vi.fn();
const mockGet = vi.fn();
vi.mock("react-router", () => ({
  useNavigate: vi.fn(() => mockPush),
  useSearchParams: vi.fn(() => [{ get: mockGet }]),
}));

const mockSetAuthenticated = vi.fn();
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({ setAuthenticated: mockSetAuthenticated })),
}));

const mockMutateAsync = vi.fn();
const mockReset = vi.fn();
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useSigninBySSO: vi.fn(() => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
    reset: mockReset,
  })),
}));

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
}));

vi.mock("@/lib/error", () => ({
  isErrorWithErrors: vi.fn(() => false),
}));

describe("useSsoActivation", () => {
  beforeEach(() => {
    vi.resetAllMocks();
    vi.stubGlobal("sessionStorage", {
      getItem: vi.fn(() => null),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });
  });

  it("should do nothing when code or state is missing", () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "" : key === "state" ? "" : null,
    );

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    expect(mockMutateAsync).not.toHaveBeenCalled();
  });

  it("should call signinBySSO and redirect to the profile page on success", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "state-token" : null,
    );
    mockMutateAsync.mockResolvedValue({
      mfa_required: false,
      access_token: "token",
    });

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    await waitFor(() =>
      expect(mockMutateAsync).toHaveBeenCalledWith({
        code: "auth-code",
        state: "state-token",
      }),
    );
    await waitFor(() => expect(mockSetAuthenticated).toHaveBeenCalled());
    expect(mockPush).toHaveBeenCalledWith("/app/profile");
  });

  it("should redirect to MFA check when MFA is enabled", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "mfa-state" : null,
    );
    mockMutateAsync.mockResolvedValue({
      error: "mfa_enabled",
      mfa_required: true,
      mfa_id: "mfa-123",
      mfa_type: 1,
    });

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    await waitFor(() =>
      expect(mockPush).toHaveBeenCalledWith("/mfa-check?mfa_id=mfa-123&mfa_type=1"),
    );
  });

  it("should redirect to login on error", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "error-state" : null,
    );
    mockMutateAsync.mockRejectedValue(new Error("SSO failed"));

    renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(mockPush).toHaveBeenCalledWith("/login"));
  });

  it("should return isPending state", () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "" : key === "state" ? "" : null,
    );

    const { result } = renderHook(() => useSsoActivation(), {
      wrapper: createWrapper(),
    });

    expect(result.current).toHaveProperty("isPending");
  });

  it("redirects to the sso-activate page when a user redirect url is returned", async () => {
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "redir-state" : null,
    );
    mockMutateAsync.mockResolvedValue({
      sso_user_redirect_url: "https://iam.test/activate?username=jane@x.com&code=sso-code",
    });

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    await waitFor(() =>
      expect(mockPush).toHaveBeenCalledWith(
        "/sso-activate?username=jane%40x.com&code=sso-code",
      ),
    );
  });

  it("shows a no-account message for a user_not_found error", async () => {
    const { showErrorToast } = await import("@/hooks/use-toast");
    mockGet.mockImplementation((key: string) =>
      key === "code" ? "auth-code" : key === "state" ? "nf-state" : null,
    );
    mockMutateAsync.mockRejectedValue({
      error: { description: "jane@x.com user_not_found" },
    });

    renderHook(() => useSsoActivation(), { wrapper: createWrapper() });

    await waitFor(() =>
      expect(showErrorToast).toHaveBeenCalledWith({
        errors: "There is no account with this email (jane@x.com).",
      }),
    );
    expect(mockPush).toHaveBeenCalledWith("/login");
  });
});
