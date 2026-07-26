import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setAuthenticated: vi.fn(),
  resend: vi.fn(),
}));

vi.mock("react-router-dom", () => ({ useNavigate: () => h.navigateMock }));
vi.mock("nuqs", () => ({
  useQueryStates: () => [{ mfa_id: "mfa-1", mfa_type: 2 }],
  parseAsString: { withDefault: () => ({}) },
  parseAsInteger: { withDefault: () => ({}) },
}));
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({ setAuthenticated: h.setAuthenticated })),
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: vi.fn() }));
vi.mock("@blocks-idp/mfa/hooks/use-resend-otp", () => ({
  useResendOtp: vi.fn(() => ({ remainingTime: 0, resend: h.resend })),
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  useOidcAuthAnimation: vi.fn(() => null),
}));

import { MfaCheckFrom } from "./mfa-check-form";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("MfaCheckFrom", () => {
  it("renders the OTP input, resend control and a disabled verify button", () => {
    const { container } = render(<MfaCheckFrom />);
    expect(container.querySelector("input")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /resend code/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /verify/i })).toBeDisabled();
  });

  it("posts the entered code to the login endpoint and surfaces an invalid-code error", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      headers: { get: () => "application/json" },
      json: async () => ({ error: "invalid_mfa_code" }),
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container } = render(<MfaCheckFrom />);
    const otp = container.querySelector("input") as HTMLInputElement;
    fireEvent.change(otp, { target: { value: "12345" } });

    const verify = screen.getByRole("button", { name: /verify/i });
    await waitFor(() => expect(verify).not.toBeDisabled());
    fireEvent.click(verify);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/oidc/login");
    const body = JSON.parse(options.body as string);
    expect(body.mfa_id).toBe("mfa-1");
    expect(body.mfa_code).toBe("12345");

    expect(
      await screen.findByText("Invalid verification code. Please try again."),
    ).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  const submitCode = async (container: HTMLElement) => {
    const otp = container.querySelector("input") as HTMLInputElement;
    fireEvent.change(otp, { target: { value: "12345" } });
    const verify = screen.getByRole("button", { name: /verify/i });
    await waitFor(() => expect(verify).not.toBeDisabled());
    fireEvent.click(verify);
  };

  it("authenticates and navigates to the console on success without a redirect", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({}),
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    await waitFor(() => expect(h.setAuthenticated).toHaveBeenCalled());
    expect(h.navigateMock).toHaveBeenCalledWith("/app/console");
    vi.unstubAllGlobals();
  });

  it("redirects when the response provides a redirect uri", async () => {
    const location = { href: "" };
    Object.defineProperty(window, "location", { value: location, configurable: true });
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({ redirect_uri: "https://redirect.test" }),
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    await waitFor(() => expect(location.href).toBe("https://redirect.test"));
    vi.unstubAllGlobals();
  });

  it("shows the account-locked message", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      headers: { get: () => "application/json" },
      json: async () => ({ error: "account_locked" }),
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    expect(await screen.findByText(/Your account is locked/)).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  it("triggers the resend handler", () => {
    render(<MfaCheckFrom />);
    fireEvent.click(screen.getByRole("button", { name: /resend code/i }));
    expect(h.resend).toHaveBeenCalled();
  });
});
