import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  data: undefined as Record<string, unknown> | undefined,
  isLoading: false,
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1", itemId: "p1" } }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => ({ data: h.data, isLoading: h.isLoading }),
  useSaveAuthConfig: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { SelfSignup } from "./self-signup";

beforeEach(() => {
  vi.clearAllMocks();
  h.data = { isSelfSignUpAllowed: false };
  h.isLoading = false;
  h.isPending = false;
});

describe("SelfSignup", () => {
  it("renders a loading skeleton while the config loads", () => {
    h.isLoading = true;
    const { container } = render(<SelfSignup />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the allow self sign-up control once loaded", () => {
    render(<SelfSignup />);
    expect(screen.getByText("Allow Self Sign-Up")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("saves the setting and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<SelfSignup />);
    fireEvent.click(screen.getByRole("checkbox"));
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when saving fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<SelfSignup />);
    fireEvent.click(screen.getByRole("checkbox"));
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });
});
