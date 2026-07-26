import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/components/breadcrumb/breadcrumb", () => ({ default: () => <div data-testid="breadcrumb" /> }));
vi.mock("@blocks-communication/mail/components/messaging/campaign-creation/campaign-creation", () => ({
  default: () => <div data-testid="campaign-creation" />,
}));
vi.mock("@blocks-communication/mail/constants/messaging", () => ({
  messagingServiceData: [
    {
      id: "m1",
      name: "Spring Campaign",
      configuration: "Primary SMTP",
      protocol: "SMTP",
      createdBy: "alice",
      createdOn: new Date("2024-01-01T00:00:00Z"),
      lastModified: new Date("2024-02-01T00:00:00Z"),
    },
  ],
}));
vi.mock("@/constants/breadcrumb-custom-title", () => ({ BREADCRUMB_CUSTOM_TITLES: {} }));

import { CampaignDetails } from "./campaign-details";

describe("CampaignDetails", () => {
  it("shows a loading placeholder when the campaign is not found", () => {
    render(<CampaignDetails params={{ id: "missing" }} />);
    expect(screen.getByText("Loading...")).toBeInTheDocument();
  });

  it("renders the campaign details for a matching id", () => {
    render(<CampaignDetails params={{ id: "m1" }} />);
    expect(screen.getByRole("heading", { name: "Spring Campaign" })).toBeInTheDocument();
    expect(screen.getByText("Primary SMTP")).toBeInTheDocument();
    expect(screen.getByText("SMTP")).toBeInTheDocument();
    expect(screen.getByText("alice")).toBeInTheDocument();
    expect(screen.getByTestId("breadcrumb")).toBeInTheDocument();
  });
});
