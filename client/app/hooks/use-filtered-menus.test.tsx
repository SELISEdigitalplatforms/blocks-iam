import { renderHook } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it } from "vitest";
import { useFilteredMenus } from "./use-filtered-menus";
import type { Menu } from "@/models/menu-models";

const wrapperAt = (path: string) => {
  const Wrapper = ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[path]}>{children}</MemoryRouter>
  );
  Wrapper.displayName = "RouterWrapper";
  return Wrapper;
};

const menu = (partial: Partial<Menu> & { id?: string }): Menu =>
  ({ label: partial.id, ...partial }) as Menu;

describe("useFilteredMenus", () => {
  it("hides project-overview menus when not on a project-overview route", () => {
    const menus: Menu[] = [
      menu({ id: "overview-project", type: "item" }),
      menu({ id: "environments", type: "item" }),
      menu({ id: "people", type: "item" }),
    ];
    const { result } = renderHook(() => useFilteredMenus(menus), {
      wrapper: wrapperAt("/app/users"),
    });
    const ids = result.current.map((m) => (m as { id: string }).id);
    expect(ids).toContain("overview-project");
    expect(ids).not.toContain("environments");
    expect(ids).not.toContain("people");
  });

  it("hides non-project menus when on a project-overview route", () => {
    const menus: Menu[] = [
      menu({ id: "overview-project", type: "item" }),
      menu({ id: "environments", type: "item" }),
    ];
    const { result } = renderHook(() => useFilteredMenus(menus), {
      wrapper: wrapperAt("/project-overview/abc"),
    });
    const ids = result.current.map((m) => (m as { id: string }).id);
    expect(ids).not.toContain("overview-project");
    expect(ids).toContain("environments");
  });

  it("drops disabled items", () => {
    const menus: Menu[] = [
      menu({ id: "a", type: "item" }),
      menu({ id: "b", type: "item", disabled: true }),
    ];
    const { result } = renderHook(() => useFilteredMenus(menus), {
      wrapper: wrapperAt("/app/users"),
    });
    const ids = result.current.map((m) => (m as { id: string }).id);
    expect(ids).toEqual(["a"]);
  });

  it("removes separators that are not flanked by two non-separator items", () => {
    const menus: Menu[] = [
      menu({ id: "sep-lead", type: "separator" }), // no previous -> dropped
      menu({ id: "a", type: "item" }),
      menu({ id: "sep-mid", type: "separator" }), // flanked -> kept
      menu({ id: "b", type: "item" }),
      menu({ id: "sep-trail", type: "separator" }), // no next -> dropped
    ];
    const { result } = renderHook(() => useFilteredMenus(menus), {
      wrapper: wrapperAt("/app/users"),
    });
    const ids = result.current.map((m) => (m as { id: string }).id);
    expect(ids).toEqual(["a", "sep-mid", "b"]);
  });

  it("keeps separator-overview after Overview on a non-project route", () => {
    const menus: Menu[] = [
      menu({ id: "overview-project", type: "item" }),
      menu({ id: "separator-overview", type: "separator" }),
      menu({ id: "environments", type: "item" }),
    ];
    const { result } = renderHook(() => useFilteredMenus(menus), {
      wrapper: wrapperAt("/app/users"),
    });
    const ids = result.current.map((m) => (m as { id: string }).id);
    expect(ids).toContain("separator-overview");
  });

  it("hides separator-overview on a project-overview route", () => {
    const menus: Menu[] = [
      menu({ id: "separator-overview", type: "separator" }),
      menu({ id: "environments", type: "item" }),
    ];
    const { result } = renderHook(() => useFilteredMenus(menus), {
      wrapper: wrapperAt("/project-overview/x"),
    });
    const ids = result.current.map((m) => (m as { id: string }).id);
    expect(ids).not.toContain("separator-overview");
  });
});
