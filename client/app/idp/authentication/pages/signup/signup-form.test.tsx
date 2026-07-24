import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  resetCaptcha: vi.fn(),
  captchaEnabled: false,
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "" }));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: undefined, captchaEnabled: h.captchaEnabled }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useSignupByEmail: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ code: "", captcha: {}, reset: h.resetCaptcha }),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  useOidcAuthAnimation: () => null,
}));
vi.mock("../login/sso-signin", () => ({
  SsoSignin: () => <div data-testid="sso-signin" />,
}));

import { SignupForm } from "./signup-form";

const renderForm = (props: Partial<Parameters<typeof SignupForm>[0]> = {}) =>
  render(
    <MemoryRouter>
      <SignupForm
        emailSignUpEnabled={props.emailSignUpEnabled ?? true}
        ssoSignUpEnabled={props.ssoSignUpEnabled ?? false}
        loginOption={props.loginOption}
        tenantId={props.tenantId ?? "tenant-1"}
      />
    </MemoryRouter>,
  );

const fillForm = () => {
  fireEvent.input(screen.getByPlaceholderText("Jane"), { target: { value: "Jane" } });
  fireEvent.input(screen.getByPlaceholderText("Doe"), { target: { value: "Doe" } });
  fireEvent.input(screen.getByPlaceholderText("name@company.com"), {
    target: { value: "jane@example.com" },
  });
  fireEvent.click(screen.getByLabelText(/I agree to the/));
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.captchaEnabled = false;
});

describe("SignupForm", () => {
  it("renders the name and email fields when email signup is enabled", () => {
    renderForm();
    expect(screen.getByPlaceholderText("Jane")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("name@company.com")).toBeInTheDocument();
  });

  it("hides the form when email signup is disabled", () => {
    renderForm({ emailSignUpEnabled: false });
    expect(screen.queryByPlaceholderText("Jane")).toBeNull();
  });

  it("renders the social login option when sso is enabled", () => {
    renderForm({
      ssoSignUpEnabled: true,
      loginOption: { ssoInfo: [{ provider: "google" }] } as unknown as Parameters<typeof SignupForm>[0]["loginOption"],
    });
    expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
  });

  it("submits the registration and navigates to the confirmation page", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();
    fillForm();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Create Account/ })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: /Create Account/ }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ email: "jane@example.com", firstName: "Jane" }),
      ),
    );
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("/signup-email-sent?email=jane@example.com"),
    );
  });

  it("shows the server error when registration fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { email: "Already exists" } });
    renderForm();
    fillForm();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Create Account/ })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: /Create Account/ }));
    await waitFor(() => expect(screen.getByText("Already exists")).toBeInTheDocument());
  });
});
