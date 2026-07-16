import { describe, expect, it } from "vitest";
import {
  isValidDomain,
  isValidSubdomain,
  getDomain,
  getSubdomain,
  getProjectBlocksApiUrl,
} from "./domain";
import type { IProject } from "@blocks-identifier/models/project.model";

describe("isValidDomain", () => {
  it("accepts fully-qualified http(s) domains", () => {
    expect(isValidDomain("https://example.com")).toBe(true);
    expect(isValidDomain("http://sub.example.co.uk")).toBe(true);
  });

  it("trims surrounding whitespace before validating", () => {
    expect(isValidDomain("  https://example.com  ")).toBe(true);
  });

  it("rejects domains without a protocol or TLD", () => {
    expect(isValidDomain("example.com")).toBe(false);
    expect(isValidDomain("https://localhost")).toBe(false);
    expect(isValidDomain("")).toBe(false);
  });
});

describe("isValidSubdomain", () => {
  it("returns false for empty input", () => {
    expect(isValidSubdomain("")).toBe(false);
  });

  it("accepts a valid label-based subdomain", () => {
    expect(isValidSubdomain("https://tenant")).toBe(true);
  });

  it("rejects labels that fail the subdomain regex", () => {
    expect(isValidSubdomain("has space")).toBe(false);
  });
});

describe("getDomain", () => {
  it("returns the registrable domain from a valid URL", () => {
    expect(getDomain("https://app.example.com")).toBe("example.com");
    expect(getDomain("https://example.com")).toBe("example.com");
  });

  it("returns an empty string for invalid input", () => {
    expect(getDomain("not a url")).toBe("");
    expect(getDomain()).toBe("");
  });
});

describe("getSubdomain", () => {
  it("returns protocol + subdomain when present", () => {
    expect(getSubdomain("https://app.example.com")).toBe("https://app");
    expect(getSubdomain("https://a.b.example.com")).toBe("https://a.b");
  });

  it("returns an empty string when there is no subdomain", () => {
    expect(getSubdomain("https://example.com")).toBe("");
  });

  it("returns an empty string for empty or invalid input", () => {
    expect(getSubdomain("")).toBe("");
    expect(getSubdomain("nonsense")).toBe("");
  });
});

describe("getProjectBlocksApiUrl", () => {
  it("returns an empty string when no project is provided", () => {
    expect(getProjectBlocksApiUrl(undefined)).toBe("");
  });

  it("returns an empty string when the default API base env is unset", () => {
    // VITE_PROJECT_DEFAULT_API_BASE_URL is not defined in the test env.
    const project = { customDomain: "" } as IProject;
    expect(getProjectBlocksApiUrl(project)).toBe("");
  });
});
