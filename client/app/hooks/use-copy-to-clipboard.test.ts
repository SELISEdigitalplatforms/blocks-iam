import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useCopyToClipboard } from "./use-copy-to-clipboard";

describe("useCopyToClipboard", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  it("writes text to the clipboard and calls onSuccess", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    const onSuccess = vi.fn();

    const { result } = renderHook(() => useCopyToClipboard());
    await act(async () => {
      await result.current.copy("hello", onSuccess);
    });

    expect(writeText).toHaveBeenCalledWith("hello");
    expect(onSuccess).toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  it("calls onError when the clipboard API is unavailable", async () => {
    vi.stubGlobal("navigator", {});
    const onError = vi.fn();

    const { result } = renderHook(() => useCopyToClipboard());
    await act(async () => {
      await result.current.copy("x", undefined, onError);
    });

    expect(onError).toHaveBeenCalledWith(expect.any(Error));
    vi.unstubAllGlobals();
  });

  it("calls onError when writeText rejects", async () => {
    const writeText = vi.fn().mockRejectedValue(new Error("denied"));
    vi.stubGlobal("navigator", { clipboard: { writeText } });
    const onError = vi.fn();

    const { result } = renderHook(() => useCopyToClipboard());
    await act(async () => {
      await result.current.copy("x", undefined, onError);
    });

    await waitFor(() => expect(onError).toHaveBeenCalledWith(expect.any(Error)));
    vi.unstubAllGlobals();
  });
});
