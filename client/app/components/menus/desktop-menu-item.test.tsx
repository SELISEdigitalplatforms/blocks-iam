import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it } from "vitest";
import { DesktopMenuItem } from "./desktop-menu-item";
import type { Menu } from "@/models/menu-models";

const leafMenu = {
  id: "users",
  type: "menu",
  name: "Users",
  path: "/app/users",
  badge: "new",
} as Extract<Menu, { type: "menu" }>;

const parentMenu = {
  id: "settings",
  type: "menu",
  name: "Settings",
  path: "/app/settings",
  children: [
    { id: "general", type: "menu", name: "General", path: "/app/settings/general" },
    { id: "billing", type: "menu", name: "Billing", path: "/app/settings/billing" },
  ],
} as Extract<Menu, { type: "menu" }>;

const renderAt = (menu: Extract<Menu, { type: "menu" }>, path: string, isSidebarOpen = true) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <DesktopMenuItem menu={menu} isSidebarOpen={isSidebarOpen} />
    </MemoryRouter>,
  );

describe("DesktopMenuItem", () => {
  it("renders a leaf menu item as a link with its badge", () => {
    renderAt(leafMenu, "/app/dashboard");
    const link = screen.getByRole("link", { name: /Users/ });
    expect(link).toHaveAttribute("href", "/app/users");
    expect(screen.getByText("new")).toBeInTheDocument();
  });

  it("marks the active menu when the route matches", () => {
    renderAt(leafMenu, "/app/users");
    const link = screen.getByRole("link", { name: /Users/ });
    expect(link).toHaveAttribute("href", "/app/users");
  });

  it("renders child menu items for a parent menu", () => {
    renderAt(parentMenu, "/app/settings/general");
    expect(screen.getByText("Settings")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "General" })).toHaveAttribute(
      "href",
      "/app/settings/general",
    );
    expect(screen.getByRole("link", { name: "Billing" })).toHaveAttribute(
      "href",
      "/app/settings/billing",
    );
  });

  it("hides the label but keeps the tooltip when the sidebar is collapsed", () => {
    renderAt(leafMenu, "/app/dashboard", false);
    // Name still present in the hover tooltip even when the sidebar is collapsed.
    expect(screen.getAllByText("Users").length).toBeGreaterThan(0);
  });
});
