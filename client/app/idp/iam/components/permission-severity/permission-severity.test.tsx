import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { PermissionSeverity } from "./permission-severity";
import type { IGetPermissionsSeverityResponse } from "@blocks-idp/iam/models/permission";

const data = [
  { severityLevel: "Critical", count: 3 },
  { severityLevel: "High", count: 12 },
] as unknown as IGetPermissionsSeverityResponse;

describe("PermissionSeverity", () => {
  it("renders a card with a tile per severity option", () => {
    render(<PermissionSeverity data={data} isLoading={false} />);
    expect(screen.getByText("Permission Severity Overview")).toBeInTheDocument();
    expect(screen.getByText("Critical Risk")).toBeInTheDocument();
    expect(screen.getByText("High Risk")).toBeInTheDocument();
    expect(screen.getByText("Medium Risk")).toBeInTheDocument();
    expect(screen.getByText("Low Risk")).toBeInTheDocument();
  });

  it("shows zero-padded counts from the data and defaults missing levels to 00", () => {
    render(<PermissionSeverity data={data} isLoading={false} />);
    expect(screen.getByText("03")).toBeInTheDocument();
    expect(screen.getByText("12")).toBeInTheDocument();
    // Medium and Low have no data entry, so both render "00".
    expect(screen.getAllByText("00").length).toBeGreaterThanOrEqual(2);
  });

  it("renders skeletons instead of counts while loading", () => {
    const { container } = render(<PermissionSeverity data={data} isLoading={true} />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
    expect(screen.queryByText("03")).not.toBeInTheDocument();
  });
});
