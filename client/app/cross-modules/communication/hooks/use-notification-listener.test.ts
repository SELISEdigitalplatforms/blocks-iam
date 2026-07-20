import { renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useNotificationListener } from "./use-notification-listener";

describe("useNotificationListener", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("should register a window event listener for the given notification name", () => {
    const addSpy = vi.spyOn(window, "addEventListener");

    renderHook(() => useNotificationListener("onCustomNotification", vi.fn()));

    expect(addSpy).toHaveBeenCalledWith("onCustomNotification", expect.any(Function));
  });

  it("should invoke the callback with event.detail when the event is dispatched", () => {
    const callback = vi.fn();

    renderHook(() => useNotificationListener("onCustomNotification", callback));

    const detail = { message: "hello", method: "onCustomNotification" };
    window.dispatchEvent(new CustomEvent("onCustomNotification", { detail }));

    expect(callback).toHaveBeenCalledTimes(1);
    expect(callback).toHaveBeenCalledWith(detail);
  });

  it("should remove the window event listener on unmount", () => {
    const removeSpy = vi.spyOn(window, "removeEventListener");

    const { unmount } = renderHook(() => useNotificationListener("onCustomNotification", vi.fn()));

    unmount();

    expect(removeSpy).toHaveBeenCalledWith("onCustomNotification", expect.any(Function));
  });

  it("should not invoke the callback after unmount", () => {
    const callback = vi.fn();

    const { unmount } = renderHook(() =>
      useNotificationListener("onCustomNotification", callback),
    );

    unmount();
    window.dispatchEvent(new CustomEvent("onCustomNotification", { detail: {} }));

    expect(callback).not.toHaveBeenCalled();
  });

  it("should re-subscribe when the notification name changes", () => {
    const addSpy = vi.spyOn(window, "addEventListener");
    const removeSpy = vi.spyOn(window, "removeEventListener");
    const callback = vi.fn();

    const { rerender } = renderHook(
      ({ name }: { name: string }) => useNotificationListener(name, callback),
      { initialProps: { name: "eventA" } },
    );

    expect(addSpy).toHaveBeenCalledWith("eventA", expect.any(Function));

    rerender({ name: "eventB" });

    expect(removeSpy).toHaveBeenCalledWith("eventA", expect.any(Function));
    expect(addSpy).toHaveBeenCalledWith("eventB", expect.any(Function));

    window.dispatchEvent(new CustomEvent("eventB", { detail: { v: 1 } }));
    expect(callback).toHaveBeenCalledWith({ v: 1 });
  });
});
