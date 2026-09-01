import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  resetCaptcha: vi.fn(),
  captchaEnabled: false,
  oidcUiConfig: undefined as unknown,
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "" }));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig, captchaEnabled: h.captchaEnabled }),
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
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";

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
  h.oidcUiConfig = undefined;
});

describe("SignupForm", () => {
  it("renders the name and email fields when email signup is enabled", () => {
    renderForm();
    expect(screen.getByPlaceholderText("Jane")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("name@company.com")).toBeInTheDocument();
  });

  it("renders tenant-defined signup labels and legal copy", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...DEFAULT_OIDC_UI_TEMPLATE,
        pages: {
          ...DEFAULT_OIDC_UI_TEMPLATE.pages,
          signup: {
            ...DEFAULT_OIDC_UI_TEMPLATE.pages.signup,
            firstNameLabel: "Given name",
            lastNameLabel: "Family name",
            emailLabel: "Business address",
            termsPrefix: "I accept",
            termsLinkText: "Service Rules",
            privacyLinkText: "Data Policy",
            submitButton: "Join Acme",
          },
        },
      },
    };
    renderForm();
    expect(screen.getByText("Given name")).toBeInTheDocument();
    expect(screen.getByText("Family name")).toBeInTheDocument();
    expect(screen.getByText("Business address")).toBeInTheDocument();
    expect(screen.getByText("Service Rules")).toBeInTheDocument();
    expect(screen.getByText("Data Policy")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Join Acme/ })).toBeInTheDocument();
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
      expect(h.navigate).toHaveBeenCalledWith(
        expect.stringContaining("/oidc/signup-email-sent"),
      ),
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

  it("shows the first error from an array error response", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["Array error first"] });
    renderForm();
    fillForm();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Create Account/ })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: /Create Account/ }));
    await waitFor(() => expect(screen.getByText("Array error first")).toBeInTheDocument());
  });

  it("shows the mapped error when registration throws with structured errors", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { email: "Thrown object error" } });
    renderForm();
    fillForm();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Create Account/ })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: /Create Account/ }));
    await waitFor(() => expect(screen.getByText("Thrown object error")).toBeInTheDocument());
  });

  it("shows a generic error when registration throws a plain value", async () => {
    h.mutateAsync.mockRejectedValue("plain failure");
    renderForm();
    fillForm();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Create Account/ })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: /Create Account/ }));
    await waitFor(() => expect(screen.getByText("Something went wrong")).toBeInTheDocument());
  });
});
