import { vi } from "vitest";

/*
 * Helpers for driving input faster than React settles, as a browser, a password manager or a
 * paste stream does (blocks-iam#682). Testing Library's act() flushes each event on its own and
 * would hide nested-update problems, so these run with the act environment switched off.
 */

type ActGlobal = typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean };

/** What a browser (and a password manager) does: set the native value, then fire `input`. */
export const nativeValueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
export const typeNative = (input: HTMLInputElement, value: string) => {
  nativeValueSetter.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
};

/** Collects every "Maximum update depth" report, wherever React sends it. */
export const captureDepthErrors = () => {
  const seen: unknown[] = [];
  const isDepth = (value: unknown) => /Maximum update depth|#185/.test(String(value));
  const onError = (event: ErrorEvent) => {
    if (isDepth(event.error ?? event.message)) {
      seen.push(event.error ?? event.message);
      event.preventDefault();
    }
  };
  const onProcess = (error: unknown) => {
    if (isDepth(error)) seen.push(error);
  };
  const originalConsoleError = console.error;
  const consoleSpy = vi.spyOn(console, "error").mockImplementation((...args: unknown[]) => {
    if (args.some(isDepth)) {
      seen.push(args[0]);
      return;
    }
    originalConsoleError(...args);
  });
  globalThis.addEventListener("error", onError);
  process.on("uncaughtException", onProcess);
  process.on("unhandledRejection", onProcess);
  return {
    seen,
    stop: () => {
      globalThis.removeEventListener("error", onError);
      process.off("uncaughtException", onProcess);
      process.off("unhandledRejection", onProcess);
      consoleSpy.mockRestore();
    },
  };
};

export const settle = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/**
 * Runs `burst` with React's act environment off, the way the browser runs events, then waits
 * for every scheduled render to settle. Returns the depth errors seen.
 */
export const runOutsideAct = async (burst: () => Promise<void> | void) => {
  const g = globalThis as ActGlobal;
  const previous = g.IS_REACT_ACT_ENVIRONMENT;
  const capture = captureDepthErrors();
  g.IS_REACT_ACT_ENVIRONMENT = false;
  try {
    await burst();
    await settle(300);
  } finally {
    g.IS_REACT_ACT_ENVIRONMENT = previous;
    capture.stop();
  }
  return capture.seen;
};
