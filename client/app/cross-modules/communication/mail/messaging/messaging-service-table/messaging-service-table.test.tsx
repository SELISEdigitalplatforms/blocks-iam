import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ navigateMock: vi.fn() }));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
// Heavy sibling widgets are out of scope for this table's behavior.
vi.mock(
  "@blocks-communication/mail/components/messaging/campaign-creation/campaign-creation",
  () => ({ default: () => null }),
);
vi.mock(
  "@blocks-communication/mail/components/messaging/messaging-table-toolbar/messaging-table-toolbar",
  () => ({ MessagingTableToolbar: () => null }),
);
vi.mock("@/components/ui-kits/table-pagination/table-pagination", () => ({
  default: () => null,
  TablePagination: () => null,
}));

import { MessagingServiceTable } from "./messaging-service-table";

const renderTable = () =>
  render(
    <MemoryRouter>
      <MessagingServiceTable />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("MessagingServiceTable", () => {
  it("renders the header and tabs", () => {
    renderTable();
    expect(
      screen.getByRole("heading", { name: "Messaging" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Messages" })).toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Reports" })).toBeInTheDocument();
  });

  it("renders rows from the static messaging data", () => {
    renderTable();
    expect(screen.getByText("Reset Password")).toBeInTheDocument();
    expect(screen.getByText("Randy Franci")).toBeInTheDocument();
  });

  it("navigates to the campaign detail when a row is clicked", () => {
    renderTable();
    fireEvent.click(screen.getByText("Reset Password"));
    expect(h.navigateMock).toHaveBeenCalledWith(
      "/utilities/messaging/campaigns/1",
    );
  });
});
