import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  createMagicUrl: vi.fn(),
  toast: vi.fn(),
  projectStore: { selectedProject: { tenantId: "t1", itemId: "p1" } },
  authStore: { user: { sub: "u1" } },
}));

vi.mock("@blocks-utilities/hooks/use-magic-url", () => ({
  useCreateMagicUrl: vi.fn(() => ({
    mutate: h.createMagicUrl,
    isPending: false,
  })),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => h.projectStore),
}));
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => h.authStore),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: h.toast }));

import { MagicUrlDialog } from "./magic-url-dialog";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("MagicUrlDialog", () => {
  it("renders the dialog with URI/Name fields and a disabled Create button", () => {
    render(<MagicUrlDialog open onOpenChange={vi.fn()} />);

    expect(screen.getByText("Magic URL")).toBeInTheDocument();
    expect(
      screen.getByText("Create a new Magic URL with custom configurations."),
    ).toBeInTheDocument();
    expect(screen.getByText("URI *")).toBeInTheDocument();
    expect(screen.getByText("Name *")).toBeInTheDocument();
    // Invalid form => Create is disabled.
    expect(screen.getByRole("button", { name: "Create" })).toBeDisabled();
  });

  it("closes via Cancel", () => {
    const onOpenChange = vi.fn();
    render(<MagicUrlDialog open onOpenChange={onOpenChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("creates a magic url with the typed values once valid", async () => {
    render(<MagicUrlDialog open onOpenChange={vi.fn()} />);

    const uri = document.querySelector("#url") as HTMLInputElement;
    const name = document.querySelector("#name") as HTMLInputElement;

    fireEvent.change(uri, { target: { value: "example.com" } });
    fireEvent.change(name, { target: { value: "My Link" } });

    const createBtn = screen.getByRole("button", { name: "Create" });
    await waitFor(() => expect(createBtn).toBeEnabled());

    fireEvent.click(createBtn);

    expect(h.createMagicUrl).toHaveBeenCalled();
    const payload = h.createMagicUrl.mock.calls[0][0];
    expect(payload.uri).toBe("example.com");
    expect(payload.name).toBe("My Link");
    expect(payload.projectKey).toBe("t1");
    expect(payload.requestByUserId).toBe("u1");
  });
});
