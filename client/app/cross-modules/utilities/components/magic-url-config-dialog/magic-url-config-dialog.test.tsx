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
});
