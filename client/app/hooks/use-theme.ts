// Theme is managed centrally by blocks-kit's cookie-backed app-settings store,
// which is the same source of truth the shared console header's theme switcher
// uses. Re-export from blocks-kit so blocks-iam has a single theme system.
//
// A previous local ThemeProvider persisted to localStorage while the header
// toggle wrote the cookie; on reload the two disagreed (toggle showed Dark but
// the UI rendered Light). Consolidating on blocks-kit removes that split-brain.
export { ThemeProvider } from "@seliseblocks/blocks-kit/providers";
export { useTheme } from "@seliseblocks/blocks-kit/hooks";
export type { Theme } from "@seliseblocks/blocks-kit/hooks";
