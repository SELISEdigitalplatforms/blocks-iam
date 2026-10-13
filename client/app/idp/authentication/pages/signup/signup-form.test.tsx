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
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const renderForm = (props: Partial<Parameters<typeof SignupForm>[0]> = {}) =>
  render(
    <MemoryRouter>
      <SignupForm
        emailSignUpEnabled={props.emailSignUpEnabled ?? true}
        ssoSignUpEnabled={props.ssoSignUpEnabled ?? false}
        loginOption={props.loginOption}
        tenantId={props.tenantId ?? "tenant-1"}
        collectOrganizationName={props.collectOrganizationName}
        signupAutoJoinEnabled={props.signupAutoJoinEnabled}
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
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
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
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          signup: {
            ...OIDC_UI_TEMPLATE_FIXTURE.pages.signup,
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

  // ---------- Join-or-create ----------

  describe("organization choice", () => {
    const COPY = {
      legend: "How would you like to start?",
      join: "Join this project's organization",
      create: "Create my own organization",
      unavailable: "This project's organization isn't available right now.",
    };
    const submitButton = () => screen.getByRole("button", { name: /Create Account/ });
    const nameField = () => screen.queryByPlaceholderText("Acme Inc.");
    const submit = async () => {
      await waitFor(() => expect(submitButton()).toBeEnabled());
      fireEvent.click(submitButton());
    };

    it("H12: offers the choice with Join preselected and no name field when auto-join and creation are on", () => {
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: true });

      const group = screen.getByRole("group", { name: COPY.legend });
      expect(group.tagName).toBe("FIELDSET");
      expect(screen.getByRole("radio", { name: COPY.join })).toBeChecked();
      expect(screen.getByRole("radio", { name: COPY.create })).not.toBeChecked();
      expect(nameField()).toBeNull();
    });

    it("H13: Create reveals a required name field; Join hides it and drops its value and errors", async () => {
      h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { name_already_exists: "Name taken" } });
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: true });
      fillForm();

      fireEvent.click(screen.getByRole("radio", { name: COPY.create }));
      expect(nameField()).toBeInTheDocument();
      // Required: the form cannot be submitted until a name is typed.
      await waitFor(() => expect(submitButton()).toBeDisabled());

      fireEvent.input(nameField()!, { target: { value: "Acme" } });
      await submit();
      await waitFor(() => expect(nameField()).toHaveAttribute("aria-invalid", "true"));

      fireEvent.click(screen.getByRole("radio", { name: COPY.join }));
      expect(nameField()).toBeNull();
      await waitFor(() => expect(submitButton()).toBeEnabled());

      fireEvent.click(screen.getByRole("radio", { name: COPY.create }));
      expect(nameField()).toHaveValue("");
      expect(nameField()).toHaveAttribute("aria-invalid", "false");
    });

    it("H14: Join passes createOrganizationDuringSignup=false and no name to the hook", async () => {
      h.mutateAsync.mockResolvedValue({ isSuccess: true });
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: true });
      fillForm();

      await submit();

      await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledTimes(1));
      const payload = h.mutateAsync.mock.calls[0][0];
      // The service layer drops a false flag from the wire (auth.service signupByEmail).
      expect(payload.createOrganizationDuringSignup).toBe(false);
      expect(payload.organizationName).toBeUndefined();
      expect(payload).not.toHaveProperty("organizationChoice");
    });

    it("H14: Create sends the creation flag with the name", async () => {
      h.mutateAsync.mockResolvedValue({ isSuccess: true });
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: true });
      fillForm();
      fireEvent.click(screen.getByRole("radio", { name: COPY.create }));
      fireEvent.input(nameField()!, { target: { value: "AJ Org 2" } });

      await submit();

      await waitFor(() =>
        expect(h.mutateAsync).toHaveBeenCalledWith(
          expect.objectContaining({ createOrganizationDuringSignup: true, organizationName: "AJ Org 2" }),
        ),
      );
    });

    it("H15: auto-join on and creation off renders no organization UI", () => {
      renderForm({ collectOrganizationName: false, signupAutoJoinEnabled: true });

      expect(screen.queryByRole("group", { name: COPY.legend })).toBeNull();
      expect(screen.queryByRole("radio")).toBeNull();
      expect(nameField()).toBeNull();
    });

    it("H16: auto-join off and creation on renders today's required name field and no choice", async () => {
      h.mutateAsync.mockResolvedValue({ isSuccess: true });
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: false });

      expect(screen.queryByRole("radio")).toBeNull();
      expect(screen.getByLabelText("Organization Name")).toBeInTheDocument();
      fillForm();
      await waitFor(() => expect(submitButton()).toBeDisabled());
      fireEvent.input(nameField()!, { target: { value: "Acme" } });
      await submit();

      await waitFor(() =>
        expect(h.mutateAsync).toHaveBeenCalledWith(
          expect.objectContaining({ createOrganizationDuringSignup: true, organizationName: "Acme" }),
        ),
      );
    });

    it("C7: an unavailable organization is announced, switches to Create and focuses the name field", async () => {
      h.mutateAsync
        .mockResolvedValueOnce({
          isSuccess: false,
          errors: { signup_organization_unavailable: "server sentence" },
        })
        .mockResolvedValueOnce({ isSuccess: true });
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: true });
      fillForm();

      await submit();

      expect(await screen.findByRole("alert")).toHaveTextContent(COPY.unavailable);
      expect(screen.getByRole("radio", { name: COPY.create })).toBeChecked();
      await waitFor(() => expect(nameField()).toHaveFocus());

      fireEvent.input(nameField()!, { target: { value: "AJ Org 2" } });
      await submit();

      await waitFor(() =>
        expect(h.mutateAsync).toHaveBeenLastCalledWith(
          expect.objectContaining({ createOrganizationDuringSignup: true, organizationName: "AJ Org 2" }),
        ),
      );
      await waitFor(() =>
        expect(h.navigate).toHaveBeenCalledWith(expect.stringContaining("/oidc/signup-email-sent")),
      );
    });

    it("C7: the same handling applies when the request throws with the error", async () => {
      h.mutateAsync.mockRejectedValue({ errors: { signup_organization_unavailable: "server sentence" } });
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: true });
      fillForm();

      await submit();

      expect(await screen.findByRole("alert")).toHaveTextContent(COPY.unavailable);
      expect(screen.getByRole("radio", { name: COPY.create })).toBeChecked();
    });

    it("S5: with creation off the unavailable message shows and no organization UI appears", async () => {
      h.mutateAsync.mockResolvedValue({
        isSuccess: false,
        errors: { signup_organization_unavailable: "server sentence" },
      });
      renderForm({ collectOrganizationName: false, signupAutoJoinEnabled: true });
      fillForm();

      await submit();

      expect(await screen.findByRole("alert")).toHaveTextContent(COPY.unavailable);
      expect(screen.queryByRole("radio")).toBeNull();
      expect(nameField()).toBeNull();
    });

    it("disables the radios while the sign-up is submitting", () => {
      h.isPending = true;
      renderForm({ collectOrganizationName: true, signupAutoJoinEnabled: true });

      expect(screen.getByRole("radio", { name: COPY.join })).toBeDisabled();
      expect(screen.getByRole("radio", { name: COPY.create })).toBeDisabled();
    });

    it("keeps the server-error area announced for other errors too", async () => {
      h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { email: "Already exists" } });
      renderForm();
      fillForm();

      await submit();

      expect(await screen.findByRole("alert")).toHaveTextContent("Already exists");
    });
  });
});
