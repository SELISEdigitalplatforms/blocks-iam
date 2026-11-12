import { describe, expect, it } from "vitest";
import { getAvailableSsoProviders } from "./sso-providers.util";
import type { LoginOption } from "@blocks-idp/authentication/models/auth.model";

const options = (...providers: string[]): LoginOption => ({
  allowedGrantTypes: ["social"],
  ssoInfo: providers.map((provider) => ({ provider, audience: "", clientId: `${provider}-cid` })),
});

describe("getAvailableSsoProviders", () => {
  it("returns nothing without login options", () => {
    expect(getAvailableSsoProviders(undefined)).toEqual([]);
  });

  it("lists the offered providers in the page's display order, with their client ids", () => {
    const providers = getAvailableSsoProviders(options("microsoft", "github", "google"));
    expect(providers.map((p) => [p.provider, p.clientId, p.label])).toEqual([
      ["github", "github-cid", "GitHub"],
      ["google", "google-cid", "Google"],
      ["microsoft", "microsoft-cid", "Microsoft"],
    ]);
  });

  it("leaves out a provider the page cannot show and one it does not know", () => {
    expect(getAvailableSsoProviders(options("facebook", "myspace"))).toEqual([]);
  });

  it("includes the tenant's own SSO", () => {
    expect(getAvailableSsoProviders(options("ownsso")).map((p) => p.provider)).toEqual(["ownsso"]);
  });
});
