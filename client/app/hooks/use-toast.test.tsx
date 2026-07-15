import { act, renderHook, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  reducer,
  toast,
  useToast,
  showSuccessToast,
  showInfoToast,
  showErrorToast,
} from "./use-toast";

type AnyToast = {
  id: string;
  open?: boolean;
  title?: React.ReactNode;
  description?: React.ReactNode;
  variant?: string;
};

const makeToast = (id: string, overrides: Partial<AnyToast> = {}): AnyToast => ({
  id,
  open: true,
  title: `title-${id}`,
  ...overrides,
});

describe("use-toast reducer", () => {
  it("ADD_TOAST prepends and enforces TOAST_LIMIT of 1", () => {
    const state = { toasts: [makeToast("1")] };
    const next = reducer(state as never, {
      type: "ADD_TOAST",
      toast: makeToast("2") as never,
    });
    expect(next.toasts).toHaveLength(1);
    expect(next.toasts[0].id).toBe("2");
  });

  it("UPDATE_TOAST merges into the matching toast only", () => {
    const state = { toasts: [makeToast("1", { title: "old" })] };
    const next = reducer(state as never, {
      type: "UPDATE_TOAST",
      toast: { id: "1", title: "new" } as never,
    });
    expect(next.toasts[0].title).toBe("new");

    const untouched = reducer(state as never, {
      type: "UPDATE_TOAST",
      toast: { id: "does-not-exist", title: "x" } as never,
    });
    expect(untouched.toasts[0].title).toBe("old");
  });

  it("DISMISS_TOAST with an id closes only that toast", () => {
    const state = { toasts: [makeToast("1"), makeToast("2")] };
    const next = reducer(state as never, { type: "DISMISS_TOAST", toastId: "1" });
    expect(next.toasts.find((t) => t.id === "1")?.open).toBe(false);
    expect(next.toasts.find((t) => t.id === "2")?.open).toBe(true);
  });

  it("DISMISS_TOAST without id closes every toast", () => {
    const state = { toasts: [makeToast("1"), makeToast("2")] };
    const next = reducer(state as never, { type: "DISMISS_TOAST" });
    expect(next.toasts.every((t) => t.open === false)).toBe(true);
  });

  it("REMOVE_TOAST with undefined clears all, with id filters one", () => {
    const state = { toasts: [makeToast("1"), makeToast("2")] };
    expect(reducer(state as never, { type: "REMOVE_TOAST", toastId: undefined }).toasts).toHaveLength(0);
    const filtered = reducer(state as never, { type: "REMOVE_TOAST", toastId: "1" });
    expect(filtered.toasts.map((t) => t.id)).toEqual(["2"]);
  });
});

describe("useToast + toast()", () => {
  afterEach(() => {
    // Clear any toast between tests so shared memory state does not leak.
    const { result } = renderHook(() => useToast());
    act(() => {
      result.current.dismiss();
    });
    vi.useRealTimers();
  });

  it("toast() adds a toast and returns id/dismiss/update handles", async () => {
    const { result } = renderHook(() => useToast());

    let handle: ReturnType<typeof toast> | undefined;
    act(() => {
      handle = toast({ title: "Hello", description: "world" });
    });

    expect(handle?.id).toBeDefined();
    await waitFor(() => expect(result.current.toasts).toHaveLength(1));
    expect(result.current.toasts[0].title).toBe("Hello");
    expect(result.current.toasts[0].open).toBe(true);
  });

  it("update() mutates and onOpenChange(false) dismisses", async () => {
    const { result } = renderHook(() => useToast());

    let handle: ReturnType<typeof toast> | undefined;
    act(() => {
      handle = toast({ title: "Original" });
    });
    await waitFor(() => expect(result.current.toasts[0]?.title).toBe("Original"));

    act(() => {
      handle?.update({ id: handle.id, title: "Updated" } as never);
    });
    await waitFor(() => expect(result.current.toasts[0]?.title).toBe("Updated"));

    // Simulate the underlying Radix close callback.
    act(() => {
      result.current.toasts[0].onOpenChange?.(false);
    });
    await waitFor(() => expect(result.current.toasts[0]?.open).toBe(false));
  });

  it("dismiss then timer removal empties the list", async () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useToast());
    act(() => {
      toast({ title: "Bye" });
    });
    expect(result.current.toasts).toHaveLength(1);

    act(() => {
      result.current.dismiss(result.current.toasts[0].id);
    });
    act(() => {
      vi.advanceTimersByTime(1_000_000);
    });
    expect(result.current.toasts).toHaveLength(0);
  });
});

describe("show* toast helpers", () => {
  afterEach(() => {
    const { result } = renderHook(() => useToast());
    act(() => result.current.dismiss());
  });

  it("showSuccessToast uses the success variant and default title", async () => {
    const { result } = renderHook(() => useToast());
    act(() => showSuccessToast({ description: "done" }));
    await waitFor(() => expect(result.current.toasts).toHaveLength(1));
    expect(result.current.toasts[0].variant).toBe("success");
    expect(result.current.toasts[0].title).toBe("Success");
    expect(result.current.toasts[0].description).toBe("done");
  });

  it("showInfoToast uses the info variant and honors a custom title", async () => {
    const { result } = renderHook(() => useToast());
    act(() => showInfoToast({ title: "Heads up", description: "fyi" }));
    await waitFor(() => expect(result.current.toasts).toHaveLength(1));
    expect(result.current.toasts[0].variant).toBe("info");
    expect(result.current.toasts[0].title).toBe("Heads up");
  });

  it("showErrorToast maps a string error to a destructive toast", async () => {
    const { result } = renderHook(() => useToast());
    act(() => showErrorToast({ errors: "Boom" }));
    await waitFor(() => expect(result.current.toasts).toHaveLength(1));
    expect(result.current.toasts[0].variant).toBe("destructive");
    expect(result.current.toasts[0].title).toBe("Failed");
    expect(result.current.toasts[0].description).toBe("Boom");
  });

  it("showErrorToast renders array messages from an errors object", async () => {
    const { result } = renderHook(() => useToast());
    act(() =>
      showErrorToast({
        errors: { email: "Invalid email", name: ["required", "too short"] },
      }),
    );
    await waitFor(() => expect(result.current.toasts).toHaveLength(1));
    // Multiple messages produce an array rendered as <div> elements.
    expect(Array.isArray(result.current.toasts[0].description)).toBe(true);
  });
});
