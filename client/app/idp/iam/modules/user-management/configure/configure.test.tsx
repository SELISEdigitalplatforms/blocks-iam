import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  configResult: {} as Record<string, unknown>,
  save: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/genesis-os", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/iam/hooks/use-iam-configuration", () => ({
  useGetIamConfiguration: () => h.configResult,
  useSaveIamConfiguration: () => ({ mutateAsync: h.save, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({ default: () => <div data-testid="breadcrumb" /> }));

import { Configure } from "./configure";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.configResult = {
    isLoading: false,
    data: {
      data: {
        accountActivationUrl: "https://a.test",
        accountVerificationUrl: "https://v.test",
        recoverAccountUrl: "https://r.test",
        activationUrlLifetimeInMinutes: 60,
        recoverAccountUrlLifetimeInMinutes: 60,
        logoutOnPasswordChange: false,
      },
    },
  };
});

describe("Configure", () => {
  it("renders the configuration form prefilled from data", () => {
    render(<Configure />);
    expect(screen.getByText("User Configuration")).toBeInTheDocument();
    expect((screen.getByPlaceholderText("Enter account activation url") as HTMLInputElement).value).toBe(
      "https://a.test",
    );
  });

  it("renders the loading skeletons while the config loads", () => {
    h.configResult = { isLoading: true, data: undefined };
    render(<Configure />);
    expect(screen.queryByPlaceholderText("Enter account activation url")).toBeNull();
  });

  it("saves an updated configuration and shows a success toast", async () => {
    h.save.mockResolvedValue({});
    render(<Configure />);
    fireEvent.input(screen.getByPlaceholderText("Enter account activation url"), {
      target: { value: "https://changed.test" },
    });
    await waitFor(() => expect(screen.getByRole("button", { name: /Change/ })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: /Change/ }));
    await waitFor(() =>
      expect(h.save).toHaveBeenCalledWith(expect.objectContaining({ projectKey: "tenant-1" })),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });
});
