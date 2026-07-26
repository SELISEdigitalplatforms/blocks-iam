import { describe, expect, it } from "vitest"
import {
  sanitizeInternalNavigationTarget,
  sanitizeInternalPath,
} from "./safe-navigation.util"

describe("sanitizeInternalPath", () => {
  it("accepts normal in-app paths", () => {
    expect(sanitizeInternalPath("/login")).toBe("/login")
    expect(sanitizeInternalPath("/oidc/login?clientId=abc")).toBe("/oidc/login?clientId=abc")
  })

  it("rejects protocol-relative and external URLs", () => {
    expect(sanitizeInternalPath("//evil.com")).toBe("/")
    expect(sanitizeInternalPath("https://evil.com")).toBe("/")
    expect(sanitizeInternalPath("http://evil.com/path")).toBe("/")
  })

  it("rejects backslash open-redirect forms", () => {
    expect(sanitizeInternalPath("\\\\evil.com")).toBe("/")
    expect(sanitizeInternalPath("/\\evil.com")).toBe("/")
    expect(sanitizeInternalPath("\\/evil.com")).toBe("/")
  })

  it("rejects javascript and data URLs", () => {
    expect(sanitizeInternalPath("javascript:alert(1)")).toBe("/")
    expect(sanitizeInternalPath("data:text/html,<script>alert(1)</script>")).toBe("/")
  })
})

describe("sanitizeInternalNavigationTarget", () => {
  it("preserves query strings for safe paths", () => {
    expect(sanitizeInternalNavigationTarget("/forgot-password?mode=oidc")).toBe(
      "/forgot-password?mode=oidc",
    )
  })

  it("drops unsafe paths even when a query string is present", () => {
    expect(sanitizeInternalNavigationTarget("//evil.com?x=1", "/login")).toBe("/login")
  })
})
