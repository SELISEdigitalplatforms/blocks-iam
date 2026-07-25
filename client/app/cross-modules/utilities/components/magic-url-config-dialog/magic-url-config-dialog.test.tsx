import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  configResult: {
    data: undefined as unknown,
    isLoading: false,
  },
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@blocks-utilities/hooks/use-magic-url", () => ({
  useGetMagicUrlConfig: vi.fn(() => h.configResult),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));

import { MagicUrlConfigDialog } from "./magic-url-config-dialog";

beforeEach(() => {
  vi.clearAllMocks();
  h.configResult = { data: undefined, isLoading: false };
});

describe("MagicUrlConfigDialog", () => {
  it("renders the config form fields when open", () => {
    h.configResult = { data: { config: null }, isLoading: false };
    render(
      <MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" />,
      { wrapper: createWrapper() },
    );

    expect(screen.getByText("Configure Magic URL")).toBeInTheDocument();
    expect(screen.getByText("Context Name")).toBeInTheDocument();
    expect(screen.getByText("Short URL Base")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });

  it("shows a loading spinner (no form) while the config loads", () => {
    h.configResult = { data: undefined, isLoading: true };
    render(
      <MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" />,
      { wrapper: createWrapper() },
    );
    expect(screen.queryByText("Context Name")).not.toBeInTheDocument();
  });

  it("prefills the inputs from an existing config", () => {
    h.configResult = {
      data: { config: { contextName: "My Context", shortUrlBase: "https://s.io/" } },
      isLoading: false,
    };
    render(
      <MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" />,
      { wrapper: createWrapper() },
    );

    expect(screen.getByDisplayValue("My Context")).toBeInTheDocument();
    expect(screen.getByDisplayValue("https://s.io/")).toBeInTheDocument();
  });

  it("validates required fields before saving", async () => {
    const onSave = vi.fn();
    h.configResult = {
      data: { config: { contextName: "", shortUrlBase: "" } },
      isLoading: false,
    };
    render(
      <MagicUrlConfigDialog
        open
        onOpenChange={vi.fn()}
        projectKey="p1"
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByText("Context name is required")).toBeInTheDocument();
    expect(screen.getByText("Short URL base is required")).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("calls onSave and shows a success toast for valid input", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    const onOpenChange = vi.fn();
    h.configResult = {
      data: {
        config: { contextName: "Ctx", shortUrlBase: "https://s.io/" },
        isSuccess: true,
      },
      isLoading: false,
    };
    render(
      <MagicUrlConfigDialog
        open
        onOpenChange={onOpenChange}
        projectKey="p1"
        onSave={onSave}
      />,
      { wrapper: createWrapper() },
    );

    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(onSave).toHaveBeenCalledWith({
        contextName: "Ctx",
        shortUrlBase: "https://s.io/",
        projectKey: "p1",
      }),
    );
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Configuration updated successfully",
    });
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("rejects an invalid short url base", async () => {
    h.configResult = {
      data: { config: { contextName: "Ctx", shortUrlBase: "not-a-url" } },
      isLoading: false,
    };
    render(<MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" />, {
      wrapper: createWrapper(),
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("URL must be a valid HTTPS/HTTP URL")).toBeInTheDocument();
  });

  it("requires the short url base to end with a slash", async () => {
    h.configResult = {
      data: { config: { contextName: "Ctx", shortUrlBase: "https://s.io" } },
      isLoading: false,
    };
    render(<MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" />, {
      wrapper: createWrapper(),
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(
      await screen.findByText("URL must end with a forward slash (/)"),
    ).toBeInTheDocument();
  });

  it("does nothing when saving without a project key", () => {
    const onSave = vi.fn();
    h.configResult = { data: { config: { contextName: "Ctx", shortUrlBase: "https://s.io/" } }, isLoading: false };
    render(<MagicUrlConfigDialog open onOpenChange={vi.fn()} onSave={onSave} />, {
      wrapper: createWrapper(),
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(onSave).not.toHaveBeenCalled();
  });

  it("shows an error toast when the saved config is unsuccessful", async () => {
    const onSave = vi.fn().mockResolvedValue(undefined);
    h.configResult = {
      data: {
        config: { contextName: "Ctx", shortUrlBase: "https://s.io/" },
        isSuccess: false,
        errorMessage: "save failed",
      },
      isLoading: false,
    };
    render(<MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" onSave={onSave} />, {
      wrapper: createWrapper(),
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "save failed" }));
  });

  it("logs an error when onSave throws", async () => {
    const errSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const onSave = vi.fn().mockRejectedValue(new Error("boom"));
    h.configResult = {
      data: { config: { contextName: "Ctx", shortUrlBase: "https://s.io/" }, isSuccess: true },
      isLoading: false,
    };
    render(<MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" onSave={onSave} />, {
      wrapper: createWrapper(),
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(errSpy).toHaveBeenCalledWith("Failed to save config:", expect.any(Error)),
    );
    errSpy.mockRestore();
  });

  it("clears field errors as the user edits the inputs", async () => {
    h.configResult = { data: { config: { contextName: "", shortUrlBase: "" } }, isLoading: false };
    render(<MagicUrlConfigDialog open onOpenChange={vi.fn()} projectKey="p1" onSave={vi.fn()} />, {
      wrapper: createWrapper(),
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(await screen.findByText("Context name is required")).toBeInTheDocument();

    fireEvent.change(screen.getByPlaceholderText("Enter context name"), { target: { value: "X" } });
    fireEvent.change(screen.getByPlaceholderText("e.g., https://short.seliseblocks.com/"), {
      target: { value: "https://x.io/" },
    });
    await waitFor(() => expect(screen.queryByText("Context name is required")).not.toBeInTheDocument());
  });

  it("opens via the trigger element", () => {
    const onOpenChange = vi.fn();
    render(
      <MagicUrlConfigDialog
        open={false}
        onOpenChange={onOpenChange}
        projectKey="p1"
        trigger={<span>open-config</span>}
      />,
      { wrapper: createWrapper() },
    );
    fireEvent.click(screen.getByText("open-config"));
    expect(onOpenChange).toHaveBeenCalledWith(true);
  });
});
