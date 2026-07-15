import { afterEach, describe, expect, it, vi } from "vitest";
import { getDefaultShortUrlBase, isValidUrl, magicUrlSchema } from "./url.util";
import * as runtimeEnv from "@/lib/runtime-env";
import { SHORT_URL_BASES } from "@blocks-utilities/constants/endpoint.constant";

describe("getDefaultShortUrlBase", () => {
  afterEach(() => vi.restoreAllMocks());

  it("falls back to the prod base when the IAM base URL is empty", () => {
    vi.spyOn(runtimeEnv, "getRuntimeEnv").mockReturnValue("");
    expect(getDefaultShortUrlBase()).toBe(SHORT_URL_BASES.prod);
  });

  it("selects the dev base when the IAM base URL references dev", () => {
    vi.spyOn(runtimeEnv, "getRuntimeEnv").mockReturnValue(
      "https://dev-iam.blocksdevelopers.com",
    );
    expect(getDefaultShortUrlBase()).toBe(SHORT_URL_BASES.dev);
  });

  it("selects the staging base when the IAM base URL references stg", () => {
    // Host deliberately avoids the substring "dev" (which also appears in
    // "blocksdevelopers") so the stg branch is the first substring match.
    vi.spyOn(runtimeEnv, "getRuntimeEnv").mockReturnValue("https://stg-iam.example.com");
    expect(getDefaultShortUrlBase()).toBe(SHORT_URL_BASES.stg);
  });
});

describe("isValidUrl", () => {
  it("accepts http and https URLs", () => {
    expect(isValidUrl("https://example.com")).toBe(true);
    expect(isValidUrl("http://example.com/path?q=1")).toBe(true);
  });

  it("rejects non-http(s) protocols", () => {
    expect(isValidUrl("ftp://example.com")).toBe(false);
    expect(isValidUrl("mailto:a@b.com")).toBe(false);
  });

  it("rejects unparseable strings", () => {
    expect(isValidUrl("not a url")).toBe(false);
    expect(isValidUrl("")).toBe(false);
  });
});

describe("magicUrlSchema", () => {
  it("accepts a valid uri + name", () => {
    const result = magicUrlSchema.safeParse({ uri: "https://example.com", name: "My Link" });
    expect(result.success).toBe(true);
  });

  it("accepts a bare domain uri without protocol", () => {
    const result = magicUrlSchema.safeParse({ uri: "example.com", name: "Link" });
    expect(result.success).toBe(true);
  });

  it("rejects an empty uri", () => {
    const result = magicUrlSchema.safeParse({ uri: "", name: "Link" });
    expect(result.success).toBe(false);
  });

  it("rejects a missing name", () => {
    const result = magicUrlSchema.safeParse({ uri: "https://example.com", name: "" });
    expect(result.success).toBe(false);
  });

  it("rejects a name longer than 100 characters", () => {
    const result = magicUrlSchema.safeParse({
      uri: "https://example.com",
      name: "a".repeat(101),
    });
    expect(result.success).toBe(false);
  });
});
