import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useServerPasswordCheck } from "./use-server-password-check";
import { serviceInstances } from "@/lib/http-client";

const post = vi.spyOn(serviceInstances.idpService, "post");

describe("useServerPasswordCheck", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    post.mockReset();
    post.mockResolvedValue({ meetsRequirements: true } as never);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  // Advances past the debounce and lets the reply's state update settle.
  const flushDebounce = async () => {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });
  };

  it("asks nothing at all when the policy needs no server check", async () => {
    renderHook(() => useServerPasswordCheck("Sunflower7!", false));
    await flushDebounce();
    expect(post).not.toHaveBeenCalled();
  });

  it("asks nothing for an empty password", async () => {
    renderHook(() => useServerPasswordCheck("", true));
    await flushDebounce();
    expect(post).not.toHaveBeenCalled();
  });

  it("reports the server's verdict once it arrives", async () => {
    const { result } = renderHook(() => useServerPasswordCheck("Sunflower7!", true));
    expect(result.current).toBeUndefined(); // unanswered, not failed
    await flushDebounce();
    expect(result.current).toBe(true);
  });

  it("reports a failure as a failure", async () => {
    post.mockResolvedValue({ meetsRequirements: false } as never);
    const { result } = renderHook(() => useServerPasswordCheck("weak", true));
    await flushDebounce();
    expect(result.current).toBe(false);
  });

  it("debounces a burst of typing into one request", async () => {
    const { rerender } = renderHook(({ pw }) => useServerPasswordCheck(pw, true), {
      initialProps: { pw: "S" },
    });
    for (const pw of ["Su", "Sun", "Sunf", "Sunfl"]) {
      rerender({ pw });
      await vi.advanceTimersByTimeAsync(50);
    }
    await flushDebounce();
    expect(post).toHaveBeenCalledTimes(1);
  });

  it("sends the password in the body, never in the URL", async () => {
    renderHook(() => useServerPasswordCheck("Sunflower7!", true));
    await flushDebounce();
    const [url, body] = post.mock.calls[0]!;
    expect(url).not.toContain("Sunflower7!");
    expect(body).toEqual({ password: "Sunflower7!" });
  });

  it("goes back to unanswered when the password changes, rather than showing a stale verdict", async () => {
    const { result, rerender } = renderHook(({ pw }) => useServerPasswordCheck(pw, true), {
      initialProps: { pw: "Sunflower7!" },
    });
    await flushDebounce();
    expect(result.current).toBe(true);

    rerender({ pw: "Sunflower7!x" });
    expect(result.current).toBeUndefined();
  });

  it("leaves the row unanswered when the request fails", async () => {
    post.mockRejectedValue(new Error("offline"));
    const { result } = renderHook(() => useServerPasswordCheck("Sunflower7!", true));
    await flushDebounce();
    expect(result.current).toBeUndefined();
  });
});
