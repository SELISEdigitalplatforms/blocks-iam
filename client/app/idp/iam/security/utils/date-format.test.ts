import { describe, expect, it } from "vitest";
import {
  formatRelative,
  formatAbsolute,
  formatAbsoluteWithSeconds,
  formatAbsoluteUtcWithSeconds,
} from "./date-format";

describe("formatRelative", () => {
  it("returns an em dash for nullish input", () => {
    expect(formatRelative()).toBe("—");
    expect(formatRelative(null)).toBe("—");
    expect(formatRelative("")).toBe("—");
  });

  it("returns a relative, suffixed string for a valid date", () => {
    const past = new Date(Date.now() - 60 * 60 * 1000).toISOString();
    expect(formatRelative(past)).toMatch(/ago/);
  });

  it("returns the raw value when the date cannot be formatted", () => {
    // date-fns throws on an invalid date, which is caught and the input returned.
    expect(formatRelative("not-a-real-date")).toBe("not-a-real-date");
  });
});

describe("formatAbsolute", () => {
  it("returns an em dash for nullish input", () => {
    expect(formatAbsolute(null)).toBe("—");
  });

  it("formats a valid ISO date into a human string containing the year", () => {
    expect(formatAbsolute("2026-03-09T14:03:00Z")).toContain("2026");
  });
});

describe("formatAbsoluteWithSeconds", () => {
  it("returns an em dash for nullish input", () => {
    expect(formatAbsoluteWithSeconds(undefined)).toBe("—");
  });

  it("formats a valid ISO date and includes the year", () => {
    expect(formatAbsoluteWithSeconds("2026-03-09T14:03:05Z")).toContain("2026");
  });
});

describe("formatAbsoluteUtcWithSeconds", () => {
  it("returns an em dash for nullish input", () => {
    expect(formatAbsoluteUtcWithSeconds(null)).toBe("—");
  });

  it("formats in UTC and appends a UTC suffix", () => {
    const out = formatAbsoluteUtcWithSeconds("2026-03-09T14:03:05Z");
    expect(out).toContain("UTC");
    expect(out).toContain("14:03:05");
  });
});
