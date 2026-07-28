import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  config: { data: { allowedGrantTypes: ["password"] } as unknown, isLoading: false },
  mutateAsync: vi.fn(),
  isPending: false,
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
  tenantId: "t1",
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: h.tenantId, itemId: "p1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => h.config,
  useSaveAuthConfig: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));

import { GrantTypes } from "./grant-types";

beforeEach(() => {
  vi.clearAllMocks();
  h.config = { data: { allowedGrantTypes: ["password"] }, isLoading: false };
  h.isPending = false;
  h.tenantId = "t1";
});

describe("GrantTypes", () => {
  it("renders the grant type options", () => {
    render(<GrantTypes />);
    expect(screen.getByText("Grant Types")).toBeInTheDocument();
    expect(screen.getByText("Email/Password")).toBeInTheDocument();
    expect(screen.getByText("SSO")).toBeInTheDocument();
    expect(screen.getByText("Client Credential")).toBeInTheDocument();
  });

  it("shows skeletons and hides Save while loading", () => {
    h.config = { data: undefined, isLoading: true };
    render(<GrantTypes />);
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull();
  });

  it("keeps Save disabled until a change is made", () => {
    render(<GrantTypes />);
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("saves the selected grant types and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<GrantTypes />);

    fireEvent.click(screen.getByText("SSO"));
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Grant types updated successfully",
    });
  });

  it("shows an error toast when the save is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "bad" });
    render(<GrantTypes />);

    fireEvent.click(screen.getByText("SSO"));
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "bad" }));
  });
});
