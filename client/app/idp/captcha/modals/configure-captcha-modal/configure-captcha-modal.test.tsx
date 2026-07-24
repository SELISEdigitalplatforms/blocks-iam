import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { DialogTrigger } from "@/components/ui-kits/dialog/dialog";

const h = vi.hoisted(() => ({
  configsResult: {} as Record<string, unknown>,
  save: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/blocks-kit", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("../../hooks/use-captcha-config", () => ({
  useGetCaptchaConfigs: () => h.configsResult,
  useSaveCaptcha: () => ({ mutateAsync: h.save, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("./configure-general-captcha-from-field", () => ({
  ConfigureGeneralCaptchaFormField: () => <div data-testid="general-field" />,
}));
vi.mock("./configure-block-captcha-form-field", () => ({
  ConfigureBlockCaptchaFormField: () => <div data-testid="block-field" />,
}));

import { ConfigureCaptchaModal } from "./configure-captcha-modal";

const renderModal = (configuration?: Parameters<typeof ConfigureCaptchaModal>[0]["configuration"]) =>
  render(
    <ConfigureCaptchaModal configuration={configuration}>
      <DialogTrigger>Open</DialogTrigger>
    </ConfigureCaptchaModal>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.configsResult = { data: { configurations: [] }, isLoading: false, isFetching: false };
});

describe("ConfigureCaptchaModal", () => {
  it("opens the add-captcha dialog with the provider select", async () => {
    renderModal();
    fireEvent.click(screen.getByText("Open"));
    await waitFor(() => expect(screen.getByText("Add Captcha Configuration")).toBeInTheDocument());
    expect(screen.getByText("Captcha Provider")).toBeInTheDocument();
    expect(screen.getByTestId("general-field")).toBeInTheDocument();
  });

  it("shows the edit title when a configuration is supplied", async () => {
    renderModal({ provider: "recaptcha", isEnable: true } as never);
    fireEvent.click(screen.getByText("Open"));
    await waitFor(() =>
      expect(screen.getByText(/Edit Google reCAPTCHA/)).toBeInTheDocument(),
    );
  });
});
