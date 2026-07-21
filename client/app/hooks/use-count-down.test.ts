import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useCountDown } from "./use-count-down";

describe("useCountDown", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("starts at the initial value", () => {
    const { result } = renderHook(() => useCountDown(10));
    expect(result.current.remainingTime).toBe(10);
  });

  it("decrements once per second", () => {
    const { result } = renderHook(() => useCountDown(3));
    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(result.current.remainingTime).toBe(1);
  });

  it("stops decrementing once it reaches zero", () => {
    const { result } = renderHook(() => useCountDown(1));
    // Reach zero (this re-render lets the effect clear the interval).
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current.remainingTime).toBe(0);
    // Further time does not push it below zero.
    act(() => {
      vi.advanceTimersByTime(3000);
    });
    expect(result.current.remainingTime).toBe(0);
  });

  it("reset() restores to the initial value by default", () => {
    const { result } = renderHook(() => useCountDown(5));
    act(() => {
      vi.advanceTimersByTime(3000);
    });
    act(() => {
      result.current.reset();
    });
    expect(result.current.remainingTime).toBe(5);
  });

  it("reset(time) restores to the provided value", () => {
    const { result } = renderHook(() => useCountDown(5));
    act(() => {
      result.current.reset(30);
    });
    expect(result.current.remainingTime).toBe(30);
  });
});
