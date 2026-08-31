import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { extractOIDCParams, buildOIDCNavigationUrl, getCurrentOIDCParams } from "./oidc-utils";

describe("oidc-utils", () => {
  beforeEach(() => {
    // Reset window.location to a clean state
    Object.defineProperty(window, "location", {
      value: {
        search: "",
        hash: "",
        href: "http://localhost:3000/oidc/login",
      },
      writable: true,
      configurable: true,
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  // ─── extractOIDCParams ──────────────────────────────────────────────────────
  describe("extractOIDCParams", () => {
    it("should return default themeColor when no params present", () => {
      const params = extractOIDCParams();
      expect(params.themeColor).toBe("#124091");
    });

    it("should extract params from query string", () => {
      Object.defineProperty(window, "location", {
        value: {
          search:
            "?x-blocks-key=test-key&userName=testuser&clientId=client-123&brandColor=%23FF0000",
          hash: "",
          href: "http://localhost:3000/oidc/login?x-blocks-key=test-key&userName=testuser&clientId=client-123&brandColor=%23FF0000",
        },
        writable: true,
        configurable: true,
      });

      const params = extractOIDCParams();
      expect(params.projectKey).toBe("test-key");
      expect(params.userName).toBe("testuser");
      expect(params.clientId).toBe("client-123");
      expect(params.themeColor).toBe("#FF0000");
    });

    it("should extract color from hash fragment when brandColor is a hex color", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?x-blocks-key=test-key",
          hash: "#124091&logoUrl=https://cdn.test.com/logo.png",
          href: "http://localhost:3000/oidc/login?x-blocks-key=test-key#124091&logoUrl=https://cdn.test.com/logo.png",
        },
        writable: true,
        configurable: true,
      });

      const params = extractOIDCParams();
      expect(params.themeColor).toBe("#124091");
      expect(params.logoUrl).toBe("https://cdn.test.com/logo.png");
    });

    it("should extract OIDC params from hash fragment", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "",
          hash: "#124091&state=abc&nonce=def&scope=openid%20profile&redirect_uri=https://app.test.com/callback",
          href: "http://localhost:3000/oidc/login#124091&state=abc&nonce=def&scope=openid%20profile&redirect_uri=https://app.test.com/callback",
        },
        writable: true,
        configurable: true,
      });

      const params = extractOIDCParams();
      expect(params.state).toBe("abc");
      expect(params.nonce).toBe("def");
      expect(params.scope).toBe("openid profile");
      expect(params.redirectUri).toBe("https://app.test.com/callback");
    });

    it("should prefer query params over hash params", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?clientId=from-query",
          hash: "#clientId=from-hash",
          href: "http://localhost:3000/oidc/login?clientId=from-query#clientId=from-hash",
        },
        writable: true,
        configurable: true,
      });

      const params = extractOIDCParams();
      expect(params.clientId).toBe("from-query");
    });
  });

  // ─── buildOIDCNavigationUrl ─────────────────────────────────────────────────
  describe("buildOIDCNavigationUrl", () => {
    it("should build URL with current OIDC params", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?x-blocks-key=test-key&clientId=client-123&brandColor=%23FF0000",
          hash: "",
          href: "http://localhost:3000/oidc/login?x-blocks-key=test-key&clientId=client-123&brandColor=%23FF0000",
        },
        writable: true,
        configurable: true,
      });

      const url = buildOIDCNavigationUrl("/oidc/consent");
      expect(url).toContain("/oidc/consent?");
      expect(url).toContain("x-blocks-key=test-key");
      expect(url).toContain("clientId=client-123");
    });

    it("should return plain path when no OIDC params exist", () => {
      const url = buildOIDCNavigationUrl("/oidc/login");
      expect(url).toContain("/oidc/login");
    });

    it("should reject unsafe navigation paths", () => {
      const url = buildOIDCNavigationUrl("//evil.com");
      expect(url.startsWith("/oidc/login")).toBe(true);
      expect(url).not.toContain("evil.com");
    });

    // Regression: the MFA hand-off passes a path that already carries a query, and
    // joining with a second `?` folded every following param into the preceding
    // value -- `mfa_type=2?clientId=...` meant clientId was not a parameter at all.
    it("merges into a query the path already carries instead of opening a second one", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?clientId=client-123&tenant_id=tenant-9",
          hash: "",
          href: "http://localhost:3000/oidc/login?clientId=client-123&tenant_id=tenant-9",
        },
        writable: true,
        configurable: true,
      });

      const url = buildOIDCNavigationUrl("/oidc/mfa-check?mfa_id=m1&mfa_type=2");

      expect(url.split("?").length - 1).toBe(1);

      const params = new URLSearchParams(url.slice(url.indexOf("?") + 1));
      expect(params.get("mfa_id")).toBe("m1");
      expect(params.get("mfa_type")).toBe("2");
      expect(params.get("clientId")).toBe("client-123");
      expect(params.get("tenant_id")).toBe("tenant-9");
    });

    it("keeps a fragment last so the merged query never lands after the hash", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?clientId=client-123",
          hash: "",
          href: "http://localhost:3000/oidc/login?clientId=client-123",
        },
        writable: true,
        configurable: true,
      });

      const url = buildOIDCNavigationUrl("/oidc/mfa-check?mfa_id=m1#section");

      expect(url.indexOf("clientId=client-123")).toBeLessThan(url.indexOf("#section"));
      expect(url.endsWith("#section")).toBe(true);
    });
  });

  // ─── getCurrentOIDCParams ───────────────────────────────────────────────────
  describe("getCurrentOIDCParams", () => {
    it("should return URLSearchParams with current OIDC params", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "?x-blocks-key=test-key&state=abc",
          hash: "",
          href: "http://localhost:3000/oidc/login?x-blocks-key=test-key&state=abc",
        },
        writable: true,
        configurable: true,
      });

      const params = getCurrentOIDCParams();
      expect(params).toBeInstanceOf(URLSearchParams);
      expect(params.get("x-blocks-key")).toBe("test-key");
      expect(params.get("state")).toBe("abc");
    });

    it("should return empty URLSearchParams when no params exist", () => {
      const params = getCurrentOIDCParams();
      expect(params.toString()).toBe("brandColor=%23124091");
    });
  });

  // ─── hash param extraction (color + &-prefixed params) ──────────────────────
  describe("extractOIDCParams - full hash payload", () => {
    const setLocation = (search: string, hash: string) => {
      const href = `http://localhost:3000/oidc/login${search}${hash}`;
      Object.defineProperty(window, "location", {
        value: { search, hash, href },
        writable: true,
        configurable: true,
      });
    };

    it("extracts every param from an &-prefixed hash payload", () => {
      setLocation(
        "",
        "#00AABB&x-blocks-key=hk&client_id=hc&userName=hu&state=hs&nonce=hn&scope=hsc&redirect_uri=hr&tenant_id=ht&logoUrl=https%3A%2F%2Fcdn%2Flogo.png",
      );
      const p = extractOIDCParams();
      expect(p.themeColor).toBe("#00AABB");
      expect(p.projectKey).toBe("hk");
      expect(p.clientId).toBe("hc");
      expect(p.userName).toBe("hu");
      expect(p.state).toBe("hs");
      expect(p.nonce).toBe("hn");
      expect(p.scope).toBe("hsc");
      expect(p.redirectUri).toBe("hr");
      expect(p.tenantId).toBe("ht");
      expect(p.logoUrl).toBe("https://cdn/logo.png");
    });

    it("parses a hash payload that is not &-prefixed after the color", () => {
      setLocation("", "#124091clientId=nc&state=ns");
      const p = extractOIDCParams();
      // Color still recognised from the leading hex.
      expect(p.themeColor).toBe("#124091");
    });

    it("recovers brandColor from the full URL when the query value is empty", () => {
      setLocation("?brandColor=", "");
      Object.defineProperty(window, "location", {
        value: {
          search: "?brandColor=",
          hash: "",
          href: "http://localhost:3000/oidc/login?brandColor=00FF00&next=1",
        },
        writable: true,
        configurable: true,
      });
      const p = extractOIDCParams();
      expect(p.themeColor).toBe("#00FF00");
    });

    it("recovers logoUrl from the full URL and fully decodes it", () => {
      Object.defineProperty(window, "location", {
        value: {
          search: "",
          hash: "",
          href: "http://localhost:3000/oidc/login&logoUrl=https%253A%252F%252Fcdn%252Flogo.png",
        },
        writable: true,
        configurable: true,
      });
      const p = extractOIDCParams();
      expect(p.logoUrl).toBe("https://cdn/logo.png");
    });

    it("falls back to the default color for an invalid brandColor", () => {
      setLocation("?brandColor=notacolor", "");
      const p = extractOIDCParams();
      expect(p.themeColor).toBe("#124091");
    });

    it("normalises a bare 6-hex brandColor to #RRGGBB", () => {
      setLocation("?brandColor=ABCDEF", "");
      const p = extractOIDCParams();
      expect(p.themeColor).toBe("#ABCDEF");
    });

    it("returns the default color object when window is treated as undefined-safe", () => {
      // Sanity: the pure default path still yields the fallback color.
      setLocation("", "");
      expect(extractOIDCParams(true).themeColor).toBe("#124091");
    });

    it("parses clean keys from a hash that is hex-prefixed but not &-prefixed", () => {
      setLocation(
        "?brandColor=%23999999",
        "#aabbcc=1&logoUrl=http%3A%2F%2Fl&x-blocks-key=k&client_id=c&userName=u&state=s&nonce=n&scope=sc&redirect_uri=r&tenant_id=t",
      );
      const p = extractOIDCParams();
      expect(p.projectKey).toBe("k");
      expect(p.clientId).toBe("c");
      expect(p.userName).toBe("u");
      expect(p.state).toBe("s");
      expect(p.nonce).toBe("n");
      expect(p.scope).toBe("sc");
      expect(p.redirectUri).toBe("r");
      expect(p.tenantId).toBe("t");
      expect(p.logoUrl).toBe("http://l");
    });

    it("falls back to the default color when brandColor decoding throws", () => {
      setLocation("?brandColor=%25", "");
      const p = extractOIDCParams();
      expect(p.themeColor).toBe("#124091");
    });
  });

  describe("navigation helpers - full param set", () => {
    const fullSearch =
      "?x-blocks-key=k&userName=u&clientId=c&logoUrl=http%3A%2F%2Fl&brandColor=%23FF0000&state=s&nonce=n&scope=sc&redirect_uri=r&tenant_id=t";

    beforeEach(() => {
      Object.defineProperty(window, "location", {
        value: {
          search: fullSearch,
          hash: "",
          href: `http://localhost:3000/oidc/login${fullSearch}`,
        },
        writable: true,
        configurable: true,
      });
    });

    it("buildOIDCNavigationUrl includes every param", () => {
      const url = buildOIDCNavigationUrl("/oidc/consent");
      expect(url).toContain("x-blocks-key=k");
      expect(url).toContain("userName=u");
      expect(url).toContain("clientId=c");
      expect(url).toContain("logoUrl=");
      expect(url).toContain("brandColor=");
      expect(url).toContain("state=s");
      expect(url).toContain("nonce=n");
      expect(url).toContain("scope=sc");
      expect(url).toContain("redirect_uri=r");
      expect(url).toContain("tenant_id=t");
    });

    it("getCurrentOIDCParams includes every param", () => {
      const p = getCurrentOIDCParams();
      expect(p.get("x-blocks-key")).toBe("k");
      expect(p.get("userName")).toBe("u");
      expect(p.get("clientId")).toBe("c");
      expect(p.get("logoUrl")).toBe("http://l");
      expect(p.get("state")).toBe("s");
      expect(p.get("nonce")).toBe("n");
      expect(p.get("scope")).toBe("sc");
      expect(p.get("redirect_uri")).toBe("r");
      expect(p.get("tenant_id")).toBe("t");
      expect(p.get("brandColor")).toBe("#FF0000");
    });
  });
});
