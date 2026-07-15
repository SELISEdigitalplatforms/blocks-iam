import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import { useRemoveMagicUrl } from "./use-magic-url";
import { toast } from "@/hooks/use-toast";
import { useDeactivateMagicUrl } from "./use-deactivate-magic-url";

vi.mock("./use-magic-url", () => ({
  useRemoveMagicUrl: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => ({
  toast: vi.fn(),
}));

describe("useDeactivateMagicUrl", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("should call removeMagicUrl with the item id and project key", () => {
    const mutate = vi.fn();
    vi.mocked(useRemoveMagicUrl).mockReturnValue({ mutate, isPending: false } as never);

    const { result } = renderHook(() => useDeactivateMagicUrl(), { wrapper: createWrapper() });

    result.current.deactivateMagicUrl("item-1", TEST_PROJECT_KEY);

    expect(mutate).toHaveBeenCalledWith(
      { linkIds: ["item-1"], projectKey: TEST_PROJECT_KEY },
      expect.objectContaining({
        onSuccess: expect.any(Function),
        onError: expect.any(Function),
      }),
    );
  });

  it("should show a success toast and call onSuccess when the mutation succeeds", () => {
    const mutate = vi.fn((_payload, options) => options.onSuccess());
    vi.mocked(useRemoveMagicUrl).mockReturnValue({ mutate, isPending: false } as never);

    const onSuccess = vi.fn();
    const { result } = renderHook(() => useDeactivateMagicUrl(), { wrapper: createWrapper() });

    result.current.deactivateMagicUrl("item-1", TEST_PROJECT_KEY, onSuccess);

    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: "success",
        title: "Success",
        description: "Magic URL deactivated successfully",
      }),
    );
    expect(onSuccess).toHaveBeenCalledTimes(1);
  });

  it("should still succeed when no onSuccess callback is provided", () => {
    const mutate = vi.fn((_payload, options) => options.onSuccess());
    vi.mocked(useRemoveMagicUrl).mockReturnValue({ mutate, isPending: false } as never);

    const { result } = renderHook(() => useDeactivateMagicUrl(), { wrapper: createWrapper() });

    expect(() => result.current.deactivateMagicUrl("item-1", TEST_PROJECT_KEY)).not.toThrow();
    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({ variant: "success" }),
    );
  });

  it("should show a destructive toast when the mutation fails", () => {
    const mutate = vi.fn((_payload, options) => options.onError());
    vi.mocked(useRemoveMagicUrl).mockReturnValue({ mutate, isPending: false } as never);

    const onSuccess = vi.fn();
    const { result } = renderHook(() => useDeactivateMagicUrl(), { wrapper: createWrapper() });

    result.current.deactivateMagicUrl("item-1", TEST_PROJECT_KEY, onSuccess);

    expect(toast).toHaveBeenCalledWith(
      expect.objectContaining({
        variant: "destructive",
        title: "Error",
        description: "Failed to deactivate Magic URL",
      }),
    );
    expect(onSuccess).not.toHaveBeenCalled();
  });

  it("should expose isRemoving reflecting the mutation isPending state", () => {
    vi.mocked(useRemoveMagicUrl).mockReturnValue({ mutate: vi.fn(), isPending: true } as never);

    const { result } = renderHook(() => useDeactivateMagicUrl(), { wrapper: createWrapper() });

    expect(result.current.isRemoving).toBe(true);
  });
});
