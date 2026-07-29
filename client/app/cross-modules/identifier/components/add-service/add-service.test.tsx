import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  registerService: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/genesis-os", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-identifier/hooks/use-services", () => ({
  useRegisterService: () => ({ mutateAsync: h.registerService, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { AddService } from "./add-service";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("AddService", () => {
  it("opens the register-service dialog", async () => {
    render(<AddService />);
    fireEvent.click(screen.getByText("Register Service"));
    await waitFor(() => expect(screen.getByText("Register New Service")).toBeInTheDocument());
    expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument();
  });

  it("registers a service and shows a success toast", async () => {
    h.registerService.mockResolvedValue({ isSuccess: true });
    render(<AddService />);
    fireEvent.click(screen.getByText("Register Service"));
    await waitFor(() => expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument());
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "api-gateway" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.registerService).toHaveBeenCalledWith(
        expect.objectContaining({ serviceName: "api-gateway", projectKey: "tenant-1" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when registration fails", async () => {
    h.registerService.mockResolvedValue({ isSuccess: false, errors: "duplicate service" });
    render(<AddService />);
    fireEvent.click(screen.getByText("Register Service"));
    await waitFor(() => expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument());
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "api-gateway" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "duplicate service" }));
  });
});
