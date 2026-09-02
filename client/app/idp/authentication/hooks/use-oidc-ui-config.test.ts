import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useOidcUiConfig } from "./use-oidc-ui-config";
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: vi.fn(() => "") }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  extractOIDCParams: vi.fn(() => ({})),
}));

const OIDC_UI_CONFIG_ENDPOINT = "/api/idp/oidc-ui-config";
const REQUEST_OPTIONS = { absoluteUrl: true, skipBlocksKey: true };

const mockConfigWithCaptcha = {
  captcha: { key: "site-key", provider: "google", generator: "v3" },
  template: null,
};
const mockConfigWithoutCaptcha = { captcha: null, template: null };

describe("useOidcUiConfig", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getRuntimeEnv).mockReturnValue("");
  });

  it("should resolve the tenant from the override argument and set captchaEnabled", async () => {
    vi.mocked(http.get).mockResolvedValue(mockConfigWithCaptcha);

    const { result } = renderHook(() => useOidcUiConfig("tenant-x"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(http.get).toHaveBeenCalledWith(
      `${OIDC_UI_CONFIG_ENDPOINT}?tenantId=tenant-x`,
      { "X-Blocks-Key": "tenant-x" },
      REQUEST_OPTIONS,
    );
    expect(result.current.data).toEqual(mockConfigWithCaptcha);
    expect(result.current.captchaEnabled).toBe(true);
  });

  it("should fall back to an empty tenant (no query, no header) when nothing resolves", async () => {
    vi.mocked(http.get).mockResolvedValue(mockConfigWithoutCaptcha);

    const { result } = renderHook(() => useOidcUiConfig(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(http.get).toHaveBeenCalledWith(OIDC_UI_CONFIG_ENDPOINT, {}, REQUEST_OPTIONS);
    expect(result.current.captchaEnabled).toBe(false);
  });

  it("should fall back to the runtime env tenant when no override is given", async () => {
    vi.mocked(getRuntimeEnv).mockReturnValue("env-tenant");
    vi.mocked(http.get).mockResolvedValue(mockConfigWithCaptcha);

    const { result } = renderHook(() => useOidcUiConfig(), { wrapper: createWrapper() });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(http.get).toHaveBeenCalledWith(
      `${OIDC_UI_CONFIG_ENDPOINT}?tenantId=env-tenant`,
      { "X-Blocks-Key": "env-tenant" },
      REQUEST_OPTIONS,
    );
  });

  it("should surface errors", async () => {
    vi.mocked(http.get).mockRejectedValue(new Error("boom"));

    const { result } = renderHook(() => useOidcUiConfig("tenant-x"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.captchaEnabled).toBe(false);
    expect(result.current.data.template).toBeNull();
  });

  it("does not expose a template while the request is loading", () => {
    vi.mocked(http.get).mockReturnValue(new Promise(() => undefined));

    const { result } = renderHook(() => useOidcUiConfig("tenant-x"), {
      wrapper: createWrapper(),
    });

    expect(result.current.isLoading).toBe(true);
    expect(result.current.data).toEqual({
      captcha: null,
      template: null,
    });
  });

  it("uses the template returned by the public endpoint", async () => {
    const customTemplate = {
      ...OIDC_UI_TEMPLATE_FIXTURE,
      branding: { logoUrl: "https://example.test/logo.png", brandName: "Acme" },
    };
    vi.mocked(http.get).mockResolvedValue({ captcha: null, template: customTemplate });

    const { result } = renderHook(() => useOidcUiConfig("tenant-x"), {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data.template).toEqual(customTemplate);
  });
});
