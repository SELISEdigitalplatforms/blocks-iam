import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  save: vi.fn(),
  isLoading: false,
  existing: undefined as unknown,
  isJwtClaimLoading: false,
  decode: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-jwt-claim", () => ({
  useAddJwtClaim: () => ({ mutateAsync: h.save, isPending: h.isLoading }),
  useGetJwtClaim: () => ({ data: h.existing, isLoading: h.isJwtClaimLoading }),
}));
vi.mock("jwt-decode", () => ({ jwtDecode: (t: string) => h.decode(t) }));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import MapJwtClaimModal from "./map-jwt-claim-modal";

const renderModal = (open = true) =>
  render(<MapJwtClaimModal open={open} onOpenChange={vi.fn()} />);

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.existing = undefined;
  h.isJwtClaimLoading = false;
});

describe("MapJwtClaimModal", () => {
  it("renders the JWT input and the empty mapping message", () => {
    renderModal();
    expect(screen.getByText("Map JWT Claim")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("Paste here...")).toBeInTheDocument();
    expect(
      screen.getByText("Please paste a valid JWT above to view and map its fields."),
    ).toBeInTheDocument();
  });

  it("shows a validation error when decoding an empty token", () => {
    renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Decode" }));
    expect(screen.getByText("JWT is required.")).toBeInTheDocument();
  });

  it("decodes a valid token and exposes the mapping table", async () => {
    h.decode.mockReturnValue({ sub: "123", email: "a@b.com", nested: { role: "admin" } });
    renderModal();
    fireEvent.change(screen.getByPlaceholderText("Paste here..."), {
      target: { value: "header.payload.sig" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Decode" }));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(screen.getByText("JWT Key")).toBeInTheDocument();
  });

  it("shows an error when the token cannot be decoded", () => {
    h.decode.mockImplementation(() => {
      throw new Error("bad");
    });
    renderModal();
    fireEvent.change(screen.getByPlaceholderText("Paste here..."), {
      target: { value: "not-a-jwt" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Decode" }));
    expect(screen.getByText("Invalid JWT Token.")).toBeInTheDocument();
  });

  it("saves existing claim data and shows a success toast", async () => {
    h.existing = {
      itemId: "claim-1",
      userId: "sub",
      email: "email",
      name: "name",
      userName: "preferred_username",
      roles: "roles",
    };
    h.save.mockResolvedValue({ isSuccess: true });
    renderModal();
    const saveButton = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(saveButton).toBeEnabled());
    fireEvent.click(saveButton);
    await waitFor(() =>
      expect(h.save).toHaveBeenCalledWith(
        expect.objectContaining({ projectKey: "tenant-1", itemId: "claim-1" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalledWith({ description: "JWT Claim Saved Successfully" }));
  });

  it("hides the JWT input and shows the loading state while the existing claim loads", () => {
    h.isJwtClaimLoading = true;
    renderModal();
    expect(screen.getByText("Map JWT Claim")).toBeInTheDocument();
    expect(screen.queryByPlaceholderText("Paste here...")).toBeNull();
  });
});
