import { beforeEach, describe, expect, it, vi } from "vitest";

const get = vi.fn();
const post = vi.fn();

vi.mock("@/lib/http-client", () => ({
  serviceInstances: {
    idpService: { get: (...a: unknown[]) => get(...a), post: (...a: unknown[]) => post(...a) },
  },
}));

import { signupLinkService } from "./signup-link.service";
import { SIGNUP_LINK_ENDPOINTS } from "../constants/endpoint.constant";

describe("signupLinkService", () => {
  beforeEach(() => {
    get.mockReset();
    post.mockReset();
  });

  it("sends the code in X-Signup-Link-Code header for context", async () => {
    get.mockResolvedValue({ valid: false });
    await signupLinkService.getContext("secret-code", "t1");
    expect(get).toHaveBeenCalledWith(
      SIGNUP_LINK_ENDPOINTS.CONTEXT,
      expect.objectContaining({
        "X-Signup-Link-Code": "secret-code",
        "X-Blocks-Key": "t1",
      }),
      { skipBlocksKey: true },
    );
  });

  it("posts the code in the redeem body", async () => {
    post.mockResolvedValue({ authorizeUrl: "https://iam/api/oidc/authorize" });
    await signupLinkService.redeem("secret-code", "t1");
    expect(post).toHaveBeenCalledWith(
      SIGNUP_LINK_ENDPOINTS.REDEEM,
      { code: "secret-code" },
      { "X-Blocks-Key": "t1" },
      { skipBlocksKey: true },
    );
  });

  it("posts mfa id and code to redeem/mfa", async () => {
    post.mockResolvedValue({ authorizeUrl: "https://iam/api/oidc/authorize" });
    await signupLinkService.completeMfa("mfa-1", "123456", "t1");
    expect(post).toHaveBeenCalledWith(
      SIGNUP_LINK_ENDPOINTS.REDEEM_MFA,
      { mfaId: "mfa-1", mfaCode: "123456" },
      { "X-Blocks-Key": "t1" },
      { skipBlocksKey: true },
    );
  });
});
