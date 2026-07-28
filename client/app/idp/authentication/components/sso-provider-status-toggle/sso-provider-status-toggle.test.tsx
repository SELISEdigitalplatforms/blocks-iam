import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
  tenantId: "t1",
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: h.tenantId } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useUpdateSsoCredentialStatus: () => ({ mutateAsync: h.mutateAsync }),
}));

import { SSoProviderStatusToggle } from "./sso-provider-status-toggle";

const config = { itemId: "cfg-1", isDisabled: false } as ISsoProviderConfigurationWithMeta;

const renderToggle = (
  cfg: Partial<ISsoProviderConfigurationWithMeta> = {},
  setOpen = vi.fn(),
) =>
  render(
    <SSoProviderStatusToggle open setOpen={setOpen} configuration={{ ...config, ...cfg }} />,
  );

const confirm = () => fireEvent.click(screen.getByRole("button", { name: /yes/i }));

beforeEach(() => {
  vi.clearAllMocks();
  h.tenantId = "t1";
});

describe("SSoProviderStatusToggle", () => {
  it("shows the Disable prompt for an enabled provider", () => {
    renderToggle();
    expect(screen.getByText("Disable")).toBeInTheDocument();
  });

  it("shows the Enable prompt for a disabled provider", () => {
    renderToggle({ isDisabled: true });
    expect(screen.getByText("Enable")).toBeInTheDocument();
  });

  it("updates the status and shows a success toast", async () => {
    const setOpen = vi.fn();
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderToggle({}, setOpen);

    confirm();
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({
      itemId: "cfg-1",
      projectKey: "t1",
      isEnabled: true,
    });
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "SSO provider has been successfully disabled",
    });
    expect(setOpen).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when the update is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    renderToggle();
    confirm();
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("shows an error toast when the tenant id is missing", async () => {
    h.tenantId = "";
    renderToggle();
    confirm();
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });

  it("shows a generic error toast when the update throws", async () => {
    h.mutateAsync.mockRejectedValue(new Error("boom"));
    renderToggle();
    confirm();
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });
});
