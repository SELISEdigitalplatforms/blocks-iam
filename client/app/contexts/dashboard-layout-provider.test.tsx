import { render, screen, fireEvent, act } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useContext } from "react";
import { SidebarContext, DashboardLayoutProvider } from "./dashboard-layout-provider";

const h = vi.hoisted(() => ({ isMobile: false, pathname: "/dashboard" }));

vi.mock("react-router-dom", () => ({
  useLocation: () => ({ pathname: h.pathname }),
}));
vi.mock("@/hooks/use-is-mobile", () => ({ default: () => h.isMobile }));

const Consumer = () => {
  const ctx = useContext(SidebarContext);
  return (
    <div>
      <span data-testid="open">{String(ctx.isSidebarOpen)}</span>
      <span data-testid="submenu-open">{String(ctx.isSidebarSubMenuOpen)}</span>
      <span data-testid="submenu-id">{ctx.subMenuId ?? "none"}</span>
      <span data-testid="search">{ctx.servicesSearchTerm}</span>
      <button onClick={ctx.toggleSidebar}>toggle</button>
      <button onClick={ctx.closeSidebar}>close</button>
      <button onClick={ctx.closeWithoutPersist}>close-np</button>
      <button onClick={ctx.toggleSidebarSubMenu}>toggle-sub</button>
      <button onClick={ctx.showSidebarSubMenu}>show-sub</button>
      <button onClick={() => ctx.updateSubMenuId("services-1")}>set-id</button>
      <button onClick={() => ctx.updateServicesSearchTerm("query")}>set-search</button>
    </div>
  );
};

const renderProvider = (props: Partial<React.ComponentProps<typeof DashboardLayoutProvider>> = {}) =>
  render(
    <DashboardLayoutProvider isOpen={props.isOpen ?? true} persist={props.persist} storageKey={props.storageKey}>
      <Consumer />
    </DashboardLayoutProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isMobile = false;
  h.pathname = "/dashboard";
  localStorage.clear();
});

describe("DashboardLayoutProvider", () => {
  it("opens the sidebar on desktop by default (non-persist)", () => {
    renderProvider({ isOpen: false });
    expect(screen.getByTestId("open")).toHaveTextContent("true");
  });

  it("toggles the sidebar open and closed", () => {
    renderProvider();
    fireEvent.click(screen.getByText("toggle"));
    expect(screen.getByTestId("open")).toHaveTextContent("false");
    fireEvent.click(screen.getByText("toggle"));
    expect(screen.getByTestId("open")).toHaveTextContent("true");
  });

  it("persists the toggled state to localStorage when persist is enabled", () => {
    renderProvider({ persist: true, storageKey: "sk" });
    fireEvent.click(screen.getByText("toggle"));
    expect(localStorage.getItem("sk")).not.toBeNull();
  });

  it("closes the sidebar", () => {
    renderProvider();
    fireEvent.click(screen.getByText("close"));
    expect(screen.getByTestId("open")).toHaveTextContent("false");
  });

  it("updates the sub-menu id and clears the search term", () => {
    renderProvider();
    fireEvent.click(screen.getByText("set-search"));
    expect(screen.getByTestId("search")).toHaveTextContent("query");
    fireEvent.click(screen.getByText("set-id"));
    expect(screen.getByTestId("submenu-id")).toHaveTextContent("services-1");
    expect(screen.getByTestId("search")).toHaveTextContent("");
    expect(localStorage.getItem("subMenuId")).toBe("services-1");
  });

  it("shows the sub-menu when the sidebar is closed and show-sub is invoked", () => {
    renderProvider();
    fireEvent.click(screen.getByText("close-np"));
    fireEvent.click(screen.getByText("show-sub"));
    expect(screen.getByTestId("submenu-open")).toHaveTextContent("true");
    fireEvent.click(screen.getByText("toggle-sub"));
    expect(screen.getByTestId("submenu-open")).toHaveTextContent("false");
  });

  it("collapses the sidebar on mobile", () => {
    h.isMobile = true;
    renderProvider({ isOpen: true });
    expect(screen.getByTestId("open")).toHaveTextContent("false");
  });

  it("restores a persisted open state from localStorage on mount", () => {
    localStorage.setItem("sk", "true");
    act(() => {
      renderProvider({ persist: true, storageKey: "sk", isOpen: false });
    });
    expect(screen.getByTestId("open")).toHaveTextContent("true");
  });
});
