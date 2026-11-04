import { describe, expect, it, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DEFAULT_OIDC_UI_TEMPLATE, normalizeOidcUiTemplate, type IOidcUiTemplate } from "./oidc-ui-template";
import { useOidcUiConfig } from "../hooks/use-oidc-ui-config";

const h = vi.hoisted(() => ({ get: vi.fn() }));
vi.mock("@/lib/http-client", () => ({ serviceInstances: { idpService: { get: h.get } } }));

const wrapper = ({ children }: { children: React.ReactNode }) => (
  <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
    {children}
  </QueryClientProvider>
);

describe("OIDC layout defaults", () => {
  it.each([undefined, null, {}, { showAnimationPanel: undefined }, { showAnimationPanel: null }])(
    "defaults legacy layout %j to visible", (layout) => {
      const input = { branding: { brandName: "Legacy tenant" }, layout } as unknown as IOidcUiTemplate;
      const normalized = normalizeOidcUiTemplate(input);
      expect(normalized.layout.showAnimationPanel).toBe(true);
      expect(normalized.branding.brandName).toBe("Legacy tenant");
    },
  );

  it.each([true, false])("preserves the explicit value %s", (showAnimationPanel) => {
    const normalized = normalizeOidcUiTemplate({ ...DEFAULT_OIDC_UI_TEMPLATE, layout: { showAnimationPanel } });
    expect(normalized.layout.showAnimationPanel).toBe(showAnimationPanel);
    expect(DEFAULT_OIDC_UI_TEMPLATE.layout.showAnimationPanel).toBe(true);
  });

  it("keeps the panel visible while configuration loads and when no template is stored", async () => {
    let resolve!: (value: unknown) => void;
    h.get.mockReturnValue(new Promise(value => { resolve = value; }));
    const { result } = renderHook(() => useOidcUiConfig("layout-loading"), { wrapper });
    expect(result.current.data.template!.layout.showAnimationPanel).toBe(true);
    resolve({ captcha: null, template: null });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data.template!.layout.showAnimationPanel).toBe(true);
  });

  it("falls back to the visible default after a configuration request fails", async () => {
    h.get.mockRejectedValue(new Error("Config unavailable"));
    const { result } = renderHook(() => useOidcUiConfig("layout-failure"), { wrapper });
    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(result.current.data.template!.layout.showAnimationPanel).toBe(true);
  });
});
