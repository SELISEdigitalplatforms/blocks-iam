import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusBadge } from "./status-badge";
import { MailStatus } from "@blocks-communication/mail/models/email";

describe("StatusBadge", () => {
  it("renders the status text", () => {
    render(<StatusBadge status={MailStatus.Delivered} />);
    expect(screen.getByText("Delivered")).toBeInTheDocument();
  });

  it.each([
    MailStatus.Delivered,
    MailStatus.Bounced,
    MailStatus.Complained,
    MailStatus.Rejected,
    MailStatus.Received,
    MailStatus.Sent,
    "SomethingElse",
  ])("renders a badge for status %s", (status) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(status)).toBeInTheDocument();
  });
});
