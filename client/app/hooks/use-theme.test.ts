import { describe, expect, it } from "vitest";

import { ThemeProvider as SharedThemeProvider } from "@seliseblocks/genesis-os/providers";
import { useTheme as sharedUseTheme } from "@seliseblocks/genesis-os/hooks";

import { ThemeProvider, useTheme } from "./use-theme";

// The point of this module is that blocks-iam has no theme system of its own. The
// bug it was written to prevent was a local ThemeProvider persisting to localStorage
// while the shared console header wrote a cookie, so after a reload the toggle said
// Dark and the UI rendered Light. Comparing identity is what catches a reintroduced
// local implementation; a render test would pass either way.
describe("use-theme", () => {
  it("re-exports the shared provider rather than wrapping or replacing it", () => {
    expect(ThemeProvider).toBe(SharedThemeProvider);
  });

  it("re-exports the shared hook so the cookie stays the single source of truth", () => {
    expect(useTheme).toBe(sharedUseTheme);
  });
});
