import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  initiateAppLogin: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@/components/blocks-app-launcher/blocks-app-launcher", () => ({
  OS_APP: "os-app",
  initiateAppLogin: (...args: unknown[]) => h.initiateAppLogin(...args),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: h.showErrorToast }));

import { OrganizationConfig } from "./organization-config";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("OrganizationConfig", () => {
  it("renders the default trigger and initiates the OS login on click", async () => {
    h.initiateAppLogin.mockResolvedValue(undefined);
    render(<OrganizationConfig />);

    fireEvent.click(screen.getByRole("button", { name: /configure organization/i }));

    await waitFor(() => expect(h.initiateAppLogin).toHaveBeenCalled());
    expect(h.initiateAppLogin).toHaveBeenCalledWith(
      "os-app",
      "/app/t1/idp/settings?settingsTab=organization-config",
    );
  });

  it("shows an error toast when the OS login fails", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    h.initiateAppLogin.mockRejectedValue(new Error("boom"));
    render(<OrganizationConfig />);

    fireEvent.click(screen.getByRole("button", { name: /configure organization/i }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Unable to open OS. Please try again." }),
    );
    errorSpy.mockRestore();
  });

  it("wires a custom trigger element to the redirect handler", async () => {
    h.initiateAppLogin.mockResolvedValue(undefined);
    render(
      <OrganizationConfig trigger={<button>Custom Trigger</button>} />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Custom Trigger" }));
    await waitFor(() => expect(h.initiateAppLogin).toHaveBeenCalled());
  });
});
