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

  it("shows a success toast and closes on a successful creation", async () => {
    const onOpenChange = vi.fn();
    h.createMagicUrl.mockImplementation((_payload: unknown, opts: { onSuccess: () => void }) =>
      opts.onSuccess(),
    );
    render(<MagicUrlDialog open onOpenChange={onOpenChange} />);

    fireEvent.change(document.querySelector("#url") as HTMLInputElement, {
      target: { value: "example.com" },
    });
    fireEvent.change(document.querySelector("#name") as HTMLInputElement, {
      target: { value: "My Link" },
    });
    const createBtn = screen.getByRole("button", { name: "Create" });
    await waitFor(() => expect(createBtn).toBeEnabled());
    fireEvent.click(createBtn);

    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success", description: "Magic URL created successfully" }),
    );
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("shows an error toast with the error message when creation fails", async () => {
    h.createMagicUrl.mockImplementation(
      (_payload: unknown, opts: { onError: (e: unknown) => void }) =>
        opts.onError(new Error("server exploded")),
    );
    render(<MagicUrlDialog open onOpenChange={vi.fn()} />);

    fireEvent.change(document.querySelector("#url") as HTMLInputElement, {
      target: { value: "example.com" },
    });
    fireEvent.change(document.querySelector("#name") as HTMLInputElement, {
      target: { value: "My Link" },
    });
    const createBtn = screen.getByRole("button", { name: "Create" });
    await waitFor(() => expect(createBtn).toBeEnabled());
    fireEvent.click(createBtn);

    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "destructive", description: "server exploded" }),
    );
  });

  it("falls back to a generic error message for a non-Error failure", async () => {
    h.createMagicUrl.mockImplementation(
      (_payload: unknown, opts: { onError: (e: unknown) => void }) => opts.onError("nope"),
    );
    render(<MagicUrlDialog open onOpenChange={vi.fn()} />);

    fireEvent.change(document.querySelector("#url") as HTMLInputElement, {
      target: { value: "example.com" },
    });
    fireEvent.change(document.querySelector("#name") as HTMLInputElement, {
      target: { value: "My Link" },
    });
    const createBtn = screen.getByRole("button", { name: "Create" });
    await waitFor(() => expect(createBtn).toBeEnabled());
    fireEvent.click(createBtn);

    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ description: "Failed to create Magic URL" }),
    );
  });

  it("prefills action-type fields from initialData", () => {
    render(
      <MagicUrlDialog
        open
        onOpenChange={vi.fn()}
        initialData={{
          uri: "https://act.example.com",
          name: "Action Link",
          type: "0",
          requestMethod: "POST",
          usageLimit: 5,
          expiryDate: "2030-01-01T00:00:00Z",
          persistent: true,
          clientCredential: "cc",
          linkBasedActionConfigId: "cfg-1",
        }}
      />,
    );

    expect(screen.getByDisplayValue("https://act.example.com")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Action Link")).toBeInTheDocument();
    // Action type reveals the request payload/header fields.
    expect(screen.getByText("Request Payload")).toBeInTheDocument();
    expect(screen.getByText("Request Headers")).toBeInTheDocument();
    expect(screen.getByText("Encoded Query String")).toBeInTheDocument();
    // Usage limit was > 0, so its input is shown with the prefilled value.
    expect(screen.getByDisplayValue("5")).toBeInTheDocument();
  });

  it("reveals the usage-limit input when the switch is toggled on", () => {
    render(<MagicUrlDialog open onOpenChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("switch", { name: /Set Usage Limit/i }));
    expect(screen.getByPlaceholderText("Enter usage limit")).toBeInTheDocument();
  });

  it("reveals the date picker when auto-expiry is toggled on", () => {
    render(<MagicUrlDialog open onOpenChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("switch", { name: /Set Auto Expiry Date/i }));
    expect(screen.getByText("Pick a date")).toBeInTheDocument();
  });
});
