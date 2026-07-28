import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { MagicUrlStatusBadge } from "./magic-url-status-badge";
import type { MagicUrl } from "@blocks-utilities/models/magic-url.model";

const base = (overrides: Partial<MagicUrl>): MagicUrl =>
  ({ usageLimit: 0, usageCount: 0, ...overrides }) as MagicUrl;

describe("MagicUrlStatusBadge", () => {
  it("uses the explicit Active status", () => {
    render(<MagicUrlStatusBadge item={base({ status: "Active" })} />);
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("uses the explicit Disabled status", () => {
    render(<MagicUrlStatusBadge item={base({ status: "Disabled" })} />);
    expect(screen.getByText("Disabled")).toBeInTheDocument();
  });

  it("uses the explicit Expired status", () => {
    render(<MagicUrlStatusBadge item={base({ status: "Expired" })} />);
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });

  it("falls back to a manually-disabled reason", () => {
    render(<MagicUrlStatusBadge item={base({ expiredReason: "ManuallyDisabled" })} />);
    expect(screen.getByText("Disabled")).toBeInTheDocument();
  });

  it("marks usage-limit exceeded", () => {
    render(<MagicUrlStatusBadge item={base({ usageLimit: 5, usageCount: 5 })} />);
    expect(screen.getByText("Limit Exceeded")).toBeInTheDocument();
  });

  it("marks a time-expired url as Expired", () => {
    render(<MagicUrlStatusBadge item={base({ expiryDate: "2000-01-01T00:00:00Z" })} />);
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });

  it("marks an isExpired url as Expired", () => {
    render(<MagicUrlStatusBadge item={base({ isExpired: true })} />);
    expect(screen.getByText("Expired")).toBeInTheDocument();
  });

  it("defaults to Active when nothing indicates expiry", () => {
    render(<MagicUrlStatusBadge item={base({ expiryDate: "2999-01-01T00:00:00Z" })} />);
    expect(screen.getByText("Active")).toBeInTheDocument();
  });
});
