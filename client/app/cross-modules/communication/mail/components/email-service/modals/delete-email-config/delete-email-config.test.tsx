import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dialog } from "@/components/ui-kits/dialog/dialog";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  toast: vi.fn(),
}));

vi.mock("../../../../hooks/use-email-config", () => ({
  useDeleteEmailConfig: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: (a: unknown) => h.toast(a) }));

import DeleteEmailConfig from "./delete-email-config";

const renderModal = (onClose = vi.fn()) => {
  render(
    <Dialog open>
      <DeleteEmailConfig configId="cfg1" onClose={onClose} />
    </Dialog>,
  );
  return onClose;
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("DeleteEmailConfig", () => {
  it("deletes the configuration and closes on success", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const onClose = renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Delete Configuration" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith({ configurationId: "cfg1", projectKey: "t1" }),
    );
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "success" })),
    );
    expect(onClose).toHaveBeenCalled();
  });

  it("shows an error toast when the result is not successful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Delete Configuration" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });

  it("shows an error toast when the mutation throws", async () => {
    h.mutateAsync.mockRejectedValue(new Error("boom"));
    renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Delete Configuration" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });
});
