import { navigationMenus } from "./navigation-menus";
import {
  BREADCRUMB_CUSTOM_TITLES,
  BREADCRUMB_LINK_OVERRIDES,
} from "./breadcrumb-custom-title";

const collectPaths = (menus: typeof navigationMenus): string[] =>
  menus.flatMap((menu) =>
    menu.type === "menu" ? [menu.path, ...(menu.children ? collectPaths(menu.children) : [])] : [],
  );

describe("navigation after the Users and Organizations move to OS", () => {
  it("exposes no Users or Organizations menu entry", () => {
    const names = navigationMenus
      .filter((menu) => menu.type === "menu")
      .map((menu) => (menu.type === "menu" ? menu.name : ""));
    expect(names).not.toContain("Users");
    expect(names).not.toContain("Organizations");
  });

  it("links to no relocated Users or Organizations path", () => {
    const paths = collectPaths(navigationMenus);
    expect(paths).not.toContain("/app/users");
    expect(paths).not.toContain("/app/organizations");
    expect(paths.some((path) => path.startsWith("/app/user-detail"))).toBe(false);
    expect(paths.some((path) => path.startsWith("/app/organization-detail"))).toBe(false);
  });

  it("leaves no separator stranded at the end of the menu", () => {
    const last = navigationMenus[navigationMenus.length - 1];
    expect(last?.type).toBe("menu");
  });

  it("keeps the entries this frontend still owns", () => {
    const paths = collectPaths(navigationMenus);
    expect(paths).toContain("/app/dashboard");
  });

  it("drops the breadcrumb titles and link overrides for the relocated pages", () => {
    expect(BREADCRUMB_CUSTOM_TITLES).not.toHaveProperty("/app/users");
    expect(BREADCRUMB_CUSTOM_TITLES).not.toHaveProperty("/app/organizations");
    expect(BREADCRUMB_LINK_OVERRIDES).not.toHaveProperty("/app/user-detail");
    expect(BREADCRUMB_LINK_OVERRIDES).not.toHaveProperty("/app/organization-detail");
  });
});
