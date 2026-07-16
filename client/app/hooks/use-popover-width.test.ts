import { renderHook, act } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import usePopoverWidth from "./use-popover-width";

describe("usePopoverWidth", () => {
  it("should return a ref and an undefined width while no element is attached", () => {
    const { result } = renderHook(() => usePopoverWidth());

    const [ref, width] = result.current;
    expect(ref.current).toBeNull();
    expect(width).toBeUndefined();
  });

  it("should read offsetWidth from the attached element on resize", () => {
    const { result } = renderHook(() => usePopoverWidth());

    act(() => {
      (result.current[0] as { current: unknown }).current = { offsetWidth: 320 };
      window.dispatchEvent(new Event("resize"));
    });

    expect(result.current[1]).toBe(320);
  });

  it("should reflect a new width when the element size changes", () => {
    const { result } = renderHook(() => usePopoverWidth());

    act(() => {
      (result.current[0] as { current: unknown }).current = { offsetWidth: 200 };
      window.dispatchEvent(new Event("resize"));
    });
    expect(result.current[1]).toBe(200);

    act(() => {
      (result.current[0] as { current: unknown }).current = { offsetWidth: 450 };
      window.dispatchEvent(new Event("resize"));
    });
    expect(result.current[1]).toBe(450);
  });
});
