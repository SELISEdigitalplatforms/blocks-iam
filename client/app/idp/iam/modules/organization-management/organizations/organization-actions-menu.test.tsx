import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import type { IOrganization } from "@blocks-idp/iam/models/organization";

const h = vi.hoisted(() => ({
  updateProps: null as Record<string, unknown> | null,
  toggleProps: null as Record<string, unknown> | null,
}));

vi.mock("../update-organization", () => ({
  UpdateOrganization: (props: Record<string, unknown>) => {
    h.updateProps = props;
    return <div data-testid="update-org">{String(props.isOpen)}</div>;
  },
}));
vi.mock("../toggle-organization-status", () => ({
  ToggleOrganizationStatus: (props: Record<string, unknown>) => {
    h.toggleProps = props;
    return <div data-testid="toggle-status" />;
  },
}));

import { OrganizationActions } from "./organization-actions-menu";

const org = (isDisabled = false) => ({ itemId: "o1", name: "Acme", isDisabled }) as IOrganization;

const openMenu = () =>
  fireEvent.pointerDown(
    screen.getByRole("button"),
    { button: 0, ctrlKey: false, pointerType: "mouse" },
  );

describe("OrganizationActions", () => {
  it("shows the Disable action for an active organization", async () => {
    render(<OrganizationActions organization={org(false)} />);
    openMenu();
    expect(await screen.findByText("Disable")).toBeInTheDocument();
  });

  it("shows the Enable action for a disabled organization", async () => {
    render(<OrganizationActions organization={org(true)} />);
    openMenu();
    expect(await screen.findByText("Enable")).toBeInTheDocument();
  });

  it("opens the rename dialog", async () => {
    render(<OrganizationActions organization={org(false)} />);
    openMenu();
    fireEvent.click(await screen.findByText("Rename"));
    expect(await screen.findByTestId("update-org")).toHaveTextContent("true");
  });
});
