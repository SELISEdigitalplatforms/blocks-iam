import { StrictMode } from "react";
import { act, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

/*
 * Regression for blocks-iam#682 (Rollbar: React #185 "Maximum update depth exceeded" on
 * /oidc/activate/:tenantId). Renders the real Activation -> OidcAuthShell -> ActivationForm ->
 * panel PasswordStrengthChecker tree; only the network edge, captcha and navigation are mocked.
 *
 * The crash needs input events that arrive faster than each render cascade settles, which a
 * password manager or a fast paste stream produces. Testing Library's `act()` would flush every
 * event on its own and hide it, so the burst tests switch the act environment off and dispatch
 * native input events the way a browser does.
 */

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  activate: vi.fn(),
  validate: vi.fn(),
  renders: { shell: 0, form: 0, checker: 0 },
}));

vi.mock("react-router", async (importOriginal) => ({
  ...(await importOriginal<typeof import("react-router")>()),
  useNavigate: () => h.navigate,
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => null }));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "", reset: vi.fn() }),
}));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountActivation: () => ({ isPending: false, mutateAsync: h.activate }),
  useAccountActivationCodeExpiration: () => ({ isPending: false, mutateAsync: h.validate }),
  useAccountResendActivation: () => ({ isPending: false, mutateAsync: vi.fn() }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", async () => {
  const { OIDC_UI_TEMPLATE_FIXTURE } = await import(
    "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture"
  );
  const policy = {
    minLength: 8,
    maxLength: 64,
    requireUppercase: true,
    requireLowercase: true,
    requireNumbers: true,
    requireSpecialChars: true,
  };
  const data = { captcha: null, template: OIDC_UI_TEMPLATE_FIXTURE };
  return {
    useOidcUiConfig: () => ({
      data,
      captchaEnabled: false,
      collectPasswordOnActivation: true,
      passwordPolicy: policy,
      isLoading: false,
    }),
  };
});
// One AppearanceControls render per shell render.
vi.mock("@/components/appearance-controls/appearance-controls", () => ({
  AppearanceControls: () => {
    h.renders.shell++;
    return null;
  },
}));
// ActivationForm reads the animation context once per render.
vi.mock("../oidc/oidc-auth-shell", async (importOriginal) => {
  const actual = await importOriginal<typeof import("../oidc/oidc-auth-shell")>();
  return {
    ...actual,
    useOidcAuthAnimation: () => {
      h.renders.form++;
      return actual.useOidcAuthAnimation();
    },
  };
});
vi.mock("@blocks-idp/authentication/hooks/use-password-strength", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@blocks-idp/authentication/hooks/use-password-strength")>();
  return {
    ...actual,
    usePasswordStrength: (...args: Parameters<typeof actual.usePasswordStrength>) => {
      h.renders.checker++;
      return actual.usePasswordStrength(...args);
    },
  };
});

import { Activation } from "./activation";
import { runOutsideAct, settle, typeNative } from "@/test-utils/react-burst";

// The bursts drive hundreds of real renders on purpose; under a loaded CI runner that takes
// longer than the default 5 s.
const BURST_TIMEOUT_MS = 30_000;

const MET = "Abcdef1!";
const NOT_MET = "abc";

const resetCounts = () => {
  h.renders.shell = 0;
  h.renders.form = 0;
  h.renders.checker = 0;
};

const renderActivation = async (strict = false) => {
  const tree = (
    <MemoryRouter>
      <Activation code="activation-code" tenantId="tenant-1" />
    </MemoryRouter>
  );
  const view = render(strict ? <StrictMode>{tree}</StrictMode> : tree);
  await screen.findByRole("button", { name: /activate/i });
  // The panel checker is mounted once the form has pushed its inputs to the shell.
  await screen.findByRole("heading", { name: "Password Strength" });
  const [password, confirm] = Array.from(
    view.container.querySelectorAll<HTMLInputElement>('input[type="password"]'),
  );
  return { view, password, confirm };
};

beforeEach(() => {
  vi.clearAllMocks();
  resetCounts();
  h.validate.mockResolvedValue({ isSuccess: true, status: "Valid", firstName: "Grace", lastName: "Hopper" });
  h.activate.mockResolvedValue({ isSuccess: true });
});

afterEach(() => {
  vi.useRealTimers();
});

describe("Activation password step: React #185 regression (#682)", () => {
  it("survives a same-task burst of password input events", async () => {
    const { password, confirm } = await renderActivation();

    const errors = await runOutsideAct(() => {
      for (let i = 0; i < 120; i++) {
        const value = i % 2 === 0 ? MET : NOT_MET;
        typeNative(password, value);
        typeNative(confirm, value);
      }
    });

    expect(errors).toEqual([]);
  }, BURST_TIMEOUT_MS);

  it("survives a password-manager style fill stream at 0-4 ms gaps", async () => {
    const { password, confirm } = await renderActivation();

    const errors = await runOutsideAct(async () => {
      for (let i = 0; i < 120; i++) {
        const value = i % 2 === 0 ? MET : NOT_MET;
        typeNative(password, value);
        typeNative(confirm, value);
        await settle(i % 5);
      }
    });

    expect(errors).toEqual([]);
  }, BURST_TIMEOUT_MS);

  it("survives a microtask-paced character stream", async () => {
    const { password } = await renderActivation();

    const errors = await runOutsideAct(async () => {
      for (let i = 1; i <= 150; i++) {
        typeNative(password, "Ab1!".repeat(40).slice(0, i));
        await Promise.resolve();
      }
    });

    expect(errors).toEqual([]);
  }, BURST_TIMEOUT_MS);

  it("ends a fast stream with the checker and submit button in step with the last values", async () => {
    const { password, confirm } = await renderActivation();

    const errors = await runOutsideAct(() => {
      for (let i = 0; i < 100; i++) {
        typeNative(password, i % 2 === 0 ? NOT_MET : MET);
        typeNative(confirm, i % 2 === 0 ? NOT_MET : MET);
      }
    });

    expect(errors).toEqual([]);
    await waitFor(() => expect(screen.getByRole("button", { name: /activate/i })).toBeEnabled());
  }, BURST_TIMEOUT_MS);
});

describe("Activation password step: render budget (spec section 5)", () => {
  it("renders the form at most twice and the shell at most once per character typed", async () => {
    const { password, confirm } = await renderActivation();
    const typed = "Abcdef1!";

    for (const input of [password, confirm]) {
      for (let i = 1; i <= typed.length; i++) {
        resetCounts();
        await act(async () => {
          typeNative(input, typed.slice(0, i));
        });
        // The resolver validates asynchronously; let it land before counting.
        await act(async () => {
          await settle(10);
        });
        expect(h.renders.form).toBeLessThanOrEqual(2);
        expect(h.renders.shell).toBeLessThanOrEqual(1);
      }
    }
  });

  it("renders the form at most four times when both fields are filled in one tick", async () => {
    const { password, confirm } = await renderActivation();
    resetCounts();

    await act(async () => {
      typeNative(password, MET);
      typeNative(confirm, MET);
    });
    await act(async () => {
      await settle(10);
    });

    expect(h.renders.form).toBeLessThanOrEqual(4);
    await waitFor(() => expect(screen.getByRole("button", { name: /activate/i })).toBeEnabled());
  });

  it("keeps the panel checker mounted while typing instead of remounting it per keystroke", async () => {
    const { password } = await renderActivation();
    const heading = screen.getByRole("heading", { name: "Password Strength" });

    for (const value of ["A", "Ab", "Abc"]) {
      await act(async () => {
        typeNative(password, value);
      });
    }

    expect(screen.getByRole("heading", { name: "Password Strength" })).toBe(heading);
  });
});

describe("Activation password step: happy path (H1, H5)", () => {
  it("meets the requirements, enables submit and activates, under StrictMode as well", async () => {
    const { password, confirm } = await renderActivation(true);

    await act(async () => {
      typeNative(password, MET);
    });
    await act(async () => {
      typeNative(confirm, MET);
    });

    const submit = await screen.findByRole("button", { name: /activate/i });
    await waitFor(() => expect(submit).toBeEnabled());
    await act(async () => {
      submit.click();
    });

    await waitFor(() => expect(h.activate).toHaveBeenCalledOnce());
    expect(h.activate).toHaveBeenCalledWith(expect.objectContaining({ code: "activation-code", password: MET }));
  });
});
