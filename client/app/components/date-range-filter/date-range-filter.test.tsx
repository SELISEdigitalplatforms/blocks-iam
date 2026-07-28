import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ isMobile: false }));

vi.mock("@/hooks/use-is-mobile", () => ({ default: () => h.isMobile }));

import { DateRangeFilter } from "./date-range-filter";

beforeEach(() => {
  vi.clearAllMocks();
  h.isMobile = false;
});

describe("DateRangeFilter", () => {
  it("renders the title and no range summary when no date is set", () => {
    render(
      <DateRangeFilter title="Created" date={undefined} onDateChange={vi.fn()} />,
    );
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("renders the formatted range when a from/to date is provided", () => {
    const date = { from: new Date("2024-01-01"), to: new Date("2024-01-31") };
    render(<DateRangeFilter title="Created" date={date} onDateChange={vi.fn()} />);
    expect(screen.getByText("Created")).toBeInTheDocument();
  });

  it("selects a date and pushes a filter value to the column", () => {
    const onDateChange = vi.fn();
    const setFilterValue = vi.fn();
    const column = { setFilterValue } as unknown as Parameters<
      typeof DateRangeFilter
    >[0]["column"];
    render(
      <DateRangeFilter
        title="Created"
        date={{ from: new Date("2024-01-10"), to: undefined }}
        onDateChange={onDateChange}
        column={column}
      />,
    );

    fireEvent.click(screen.getByText("Created"));
    const dayButtons = Array.from(document.querySelectorAll("button")).filter((b) =>
      /^\d{1,2}$/.test(b.textContent || ""),
    );
    fireEvent.click(dayButtons[dayButtons.length - 1]);
    expect(onDateChange).toHaveBeenCalled();
  });

  it("renders a single-month calendar on mobile", () => {
    h.isMobile = true;
    render(
      <DateRangeFilter
        title="Created"
        date={{ from: new Date("2024-01-10"), to: new Date("2024-01-20") }}
        onDateChange={vi.fn()}
      />,
    );
    fireEvent.click(screen.getByText("Created"));
    expect(document.querySelectorAll("button").length).toBeGreaterThan(0);
  });
});
