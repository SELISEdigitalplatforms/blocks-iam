import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";
import { MobileMenuItem } from "./mobile-menu-item";
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

const renderAt = (menu: Extract<Menu, { type: "menu" }>, path: string, onClick = vi.fn()) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <MobileMenuItem menu={menu} onClick={onClick} />
    </MemoryRouter>,
  );

describe("MobileMenuItem", () => {
  it("renders a leaf menu item as a link with its badge", () => {
    renderAt(leafMenu, "/app/dashboard");
    expect(screen.getByRole("link", { name: /Users/ })).toHaveAttribute("href", "/app/users");
    expect(screen.getByText("new")).toBeInTheDocument();
  });

  it("calls onClick when a leaf link is clicked", () => {
    const onClick = vi.fn();
    renderAt(leafMenu, "/app/dashboard", onClick);
    fireEvent.click(screen.getByRole("link", { name: /Users/ }));
    expect(onClick).toHaveBeenCalled();
  });

  it("renders a parent menu as a trigger button and opens its child sheet", async () => {
    renderAt(parentMenu, "/app/settings/general");
    const trigger = screen.getByRole("button", { name: /Settings/ });
    expect(trigger).toBeInTheDocument();
    fireEvent.click(trigger);

    expect(await screen.findByRole("link", { name: "General" })).toHaveAttribute(
      "href",
      "/app/settings/general",
    );
    expect(screen.getByRole("link", { name: "Billing" })).toHaveAttribute(
      "href",
      "/app/settings/billing",
    );
  });
});
