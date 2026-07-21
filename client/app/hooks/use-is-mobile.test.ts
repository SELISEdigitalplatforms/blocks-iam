import { renderHook, act } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import useIsMobile from "./use-is-mobile";

const setInnerWidth = (width: number) => {
  Object.defineProperty(window, "innerWidth", {
    writable: true,
    configurable: true,
    value: width,
  });
};

describe("useIsMobile", () => {
  afterEach(() => {
    setInnerWidth(1024);
  });

  it("should report true when the width is at or below the breakpoint", () => {
    setInnerWidth(500);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(true);
  });

  it("should report false when the width is above the breakpoint", () => {
    setInnerWidth(1200);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);
  });

  it("should respect a custom breakpoint", () => {
    setInnerWidth(900);
    const { result } = renderHook(() => useIsMobile(1000));
    expect(result.current).toBe(true);
  });

  it("should update on window resize", () => {
    setInnerWidth(1200);
    const { result } = renderHook(() => useIsMobile());
    expect(result.current).toBe(false);

    act(() => {
      setInnerWidth(400);
      window.dispatchEvent(new Event("resize"));
    });

    expect(result.current).toBe(true);
  });
});
