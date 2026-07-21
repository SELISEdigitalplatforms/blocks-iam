import { describe, expect, it } from "vitest";
import { parseUserAgent, enrichWithParsedUserAgent } from "./parse-user-agent";

const UA = {
  windowsChrome:
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
  macSafari:
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15",
  iphone:
    "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
  ipad:
    "Mozilla/5.0 (iPad; CPU OS 17_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Mobile/15E148 Safari/604.1",
  androidChrome:
    "Mozilla/5.0 (Linux; Android 13; Pixel 7 Build/TQ2A.230405.003) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
  edge:
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
  firefoxLinux:
    "Mozilla/5.0 (X11; Linux x86_64; rv:121.0) Gecko/20100101 Firefox/121.0",
};

describe("parseUserAgent", () => {
  it("returns an empty object for missing user agent", () => {
    expect(parseUserAgent()).toEqual({});
    expect(parseUserAgent(null)).toEqual({});
    expect(parseUserAgent("")).toEqual({});
  });

  it("detects Windows + Chrome", () => {
    const r = parseUserAgent(UA.windowsChrome);
    expect(r.operatingSystem).toBe("Windows");
    expect(r.browser).toBe("Chrome");
    expect(r.deviceName).toBe("Windows PC");
  });

  it("detects macOS + Safari", () => {
    const r = parseUserAgent(UA.macSafari);
    expect(r.operatingSystem).toBe("macOS");
    expect(r.browser).toBe("Safari");
    expect(r.deviceName).toBe("Mac");
  });

  it("detects iPhone and iPad device names under iOS", () => {
    expect(parseUserAgent(UA.iphone).deviceName).toBe("iPhone");
    expect(parseUserAgent(UA.ipad).deviceName).toBe("iPad");
    expect(parseUserAgent(UA.iphone).operatingSystem).toBe("iOS");
  });

  it("extracts the Android device model from the Build token", () => {
    const r = parseUserAgent(UA.androidChrome);
    expect(r.operatingSystem).toBe("Android");
    expect(r.browser).toBe("Chrome");
    expect(r.deviceModel).toBe("Pixel 7");
    expect(r.deviceName).toBe("Pixel 7");
  });

  it("prefers Edge over Chrome when Edg token is present", () => {
    expect(parseUserAgent(UA.edge).browser).toBe("Edge");
  });

  it("detects Firefox on Linux", () => {
    const r = parseUserAgent(UA.firefoxLinux);
    expect(r.operatingSystem).toBe("Linux");
    expect(r.browser).toBe("Firefox");
    expect(r.deviceName).toBe("Linux device");
  });
});

describe("enrichWithParsedUserAgent", () => {
  it("returns undefined for nullish input", () => {
    expect(enrichWithParsedUserAgent(undefined)).toBeUndefined();
    expect(enrichWithParsedUserAgent(null)).toBeUndefined();
  });

  it("fills missing fields from the parsed user agent", () => {
    const enriched = enrichWithParsedUserAgent({ userAgent: UA.windowsChrome });
    expect(enriched?.operatingSystem).toBe("Windows");
    expect(enriched?.browser).toBe("Chrome");
    expect(enriched?.deviceName).toBe("Windows PC");
  });

  it("preserves explicitly-provided fields over parsed values", () => {
    const enriched = enrichWithParsedUserAgent({
      userAgent: UA.windowsChrome,
      browser: "CustomBrowser",
    });
    expect(enriched?.browser).toBe("CustomBrowser");
    expect(enriched?.operatingSystem).toBe("Windows");
  });

  it("defaults unresolved fields to null", () => {
    const enriched = enrichWithParsedUserAgent({ userAgent: "unknown-agent" });
    expect(enriched?.deviceName).toBeNull();
    expect(enriched?.operatingSystem).toBeNull();
    expect(enriched?.browser).toBeNull();
  });
});
