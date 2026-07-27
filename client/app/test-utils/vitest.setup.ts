/**
 * Global vitest setup for the jsdom environment.
 *
 * Adds jest-dom matchers and polyfills the browser globals that jsdom does not
 * provide (localStorage/sessionStorage on an opaque origin, matchMedia,
 * ResizeObserver, IntersectionObserver, scrollTo). Only patches when absent so
 * real browser-like environments and per-test overrides keep working.
 */
import "@testing-library/jest-dom/vitest";
import { beforeEach, vi } from "vitest";

// Some third-party ESM deps (e.g. framer-motion's motion-utils, pulled in via
// @seliseblocks/blocks-kit) read `process.env.NODE_ENV` at import time. Under the
// jsdom environment `process.env` can be undefined, which crashes module
// evaluation. Ensure a minimal, non-production process.env is always present.
{
  const g = globalThis as unknown as { process?: { env?: Record<string, string> } };
  if (!g.process) {
    g.process = { env: { NODE_ENV: "test" } };
  } else if (!g.process.env) {
    g.process.env = { NODE_ENV: "test" };
  } else if (g.process.env.NODE_ENV === undefined) {
    g.process.env.NODE_ENV = "test";
  }
}

class MemoryStorage implements Storage {
  private store = new Map<string, string>();

  get length(): number {
    return this.store.size;
  }

  clear(): void {
    this.store.clear();
  }

  getItem(key: string): string | null {
    return this.store.has(key) ? (this.store.get(key) as string) : null;
  }

  key(index: number): string | null {
    return Array.from(this.store.keys())[index] ?? null;
  }

  removeItem(key: string): void {
    this.store.delete(key);
  }

  setItem(key: string, value: string): void {
    this.store.set(key, String(value));
  }
}

function ensureStorage(name: "localStorage" | "sessionStorage"): void {
  let usable = false;
  try {
    const existing = (globalThis as Record<string, unknown>)[name] as
      | Storage
      | undefined;
    if (existing) {
      existing.setItem("__probe__", "1");
      existing.removeItem("__probe__");
      usable = true;
    }
  } catch {
    usable = false;
  }

  if (!usable) {
    const storage = new MemoryStorage();
    Object.defineProperty(globalThis, name, {
      value: storage,
      writable: true,
      configurable: true,
    });
    if (typeof window !== "undefined") {
      Object.defineProperty(window, name, {
        value: storage,
        writable: true,
        configurable: true,
      });
    }
  }
}

ensureStorage("localStorage");
ensureStorage("sessionStorage");

// Provide the runtime env the storage/logic services read via getRuntimeEnv
// (window.__BLOCKS_ENV__). The IAM base URL is intentionally left empty so
// IAM-scoped services produce relative URLs in tests.
if (typeof window !== "undefined") {
  (window as unknown as { __BLOCKS_ENV__?: Record<string, string> }).__BLOCKS_ENV__ = {
    BLOCKS_LOGIC_BASE_URL: "https://dev-logic.blocksdevelopers.com",
  };
}

// getRuntimeEnv() falls back to import.meta.env.BLOCKS_IAM_BASE_URL whenever
// window.__BLOCKS_ENV__ doesn't carry it (see above), and Vite's envPrefix
// loads that straight out of a developer's local client/.env — which is
// gitignored and can legitimately point at a real dev backend. Stub it back
// to empty before every test (not just once here) so a test file's own
// `afterEach(() => vi.unstubAllEnvs())` can't undo this for later tests in
// the same file — global beforeEach hooks re-run ahead of each test.
beforeEach(() => {
  vi.stubEnv("BLOCKS_IAM_BASE_URL", "");
});

if (typeof window !== "undefined" && typeof window.matchMedia !== "function") {
  Object.defineProperty(window, "matchMedia", {
    writable: true,
    configurable: true,
    value: (query: string): MediaQueryList =>
      ({
        matches: false,
        media: query,
        onchange: null,
        addListener: () => {},
        removeListener: () => {},
        addEventListener: () => {},
        removeEventListener: () => {},
        dispatchEvent: () => false,
      }) as unknown as MediaQueryList,
  });
}

if (typeof globalThis.ResizeObserver === "undefined") {
  class ResizeObserverStub {
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
  }
  (globalThis as Record<string, unknown>).ResizeObserver = ResizeObserverStub;
}

if (typeof globalThis.IntersectionObserver === "undefined") {
  class IntersectionObserverStub {
    readonly root = null;
    readonly rootMargin = "";
    readonly thresholds: ReadonlyArray<number> = [];
    observe(): void {}
    unobserve(): void {}
    disconnect(): void {}
    takeRecords(): IntersectionObserverEntry[] {
      return [];
    }
  }
  (globalThis as Record<string, unknown>).IntersectionObserver =
    IntersectionObserverStub;
}

if (typeof window !== "undefined" && typeof window.scrollTo !== "function") {
  Object.defineProperty(window, "scrollTo", {
    writable: true,
    configurable: true,
    value: () => {},
  });
}

// Radix UI and cmdk rely on Pointer Capture and scrollIntoView, neither of
// which jsdom implements. Without these, opening a Popover/Select or navigating
// a cmdk list throws. Only patch when absent so real browser-like environments
// keep their native implementations.
if (typeof Element !== "undefined") {
  if (typeof Element.prototype.hasPointerCapture !== "function") {
    Element.prototype.hasPointerCapture = () => false;
  }
  if (typeof Element.prototype.setPointerCapture !== "function") {
    Element.prototype.setPointerCapture = () => {};
  }
  if (typeof Element.prototype.releasePointerCapture !== "function") {
    Element.prototype.releasePointerCapture = () => {};
  }
  if (typeof Element.prototype.scrollIntoView !== "function") {
    Element.prototype.scrollIntoView = () => {};
  }
}
