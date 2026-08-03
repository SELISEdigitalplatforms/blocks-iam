import { renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import useRoutePathSegments from "./use-path-segments";

const routerState: { pathname: string; params: Record<string, string | undefined> } = {
  pathname: "/",
  params: {},
};

vi.mock("react-router", () => ({
  useLocation: () => ({ pathname: routerState.pathname }),
  useParams: () => routerState.params,
}));

describe("useRoutePathSegments", () => {
  beforeEach(() => {
    routerState.pathname = "/";
    routerState.params = {};
  });

  it("returns an empty list for the root path", () => {
    const { result } = renderHook(() => useRoutePathSegments());
    expect(result.current).toEqual([]);
  });

  it("builds humanized breadcrumbs from each path segment", () => {
    routerState.pathname = "/user-management/roles";
    const { result } = renderHook(() => useRoutePathSegments());
    expect(result.current).toEqual([
      { href: "/user-management", key: "/user-management", label: "User Management" },
      { href: "/user-management/roles", key: "/user-management/roles", label: "Roles" },
    ]);
  });

  it("excludes scope id segments (itemId/tenantGroupId) from keys but keeps them in hrefs", () => {
    routerState.pathname = "/organizations/tg-1/details";
    routerState.params = { tenantGroupId: "tg-1" };
    const { result } = renderHook(() => useRoutePathSegments());

    expect(result.current).toHaveLength(2);
    expect(result.current[1].href).toBe("/organizations/tg-1/details");
    // The scope id is stripped from the lookup key.
    expect(result.current[1].key).toBe("/organizations/details");
  });
});
