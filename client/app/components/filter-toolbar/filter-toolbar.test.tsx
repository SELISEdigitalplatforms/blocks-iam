import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { FilterToolbar } from "./filter-toolbar";

type Filters = { search: string; status: string };

const baseFilters = [
  { key: "search", type: "SearchInput", label: "Search" },
  { key: "status", type: "Radio", label: "Status", props: { options: [{ label: "Published", value: "pub" }] } },
] as unknown as React.ComponentProps<typeof FilterToolbar<Filters>>["filters"];

describe("FilterToolbar", () => {
  it("renders a control per filter", () => {
    render(
      <FilterToolbar<Filters>
        filters={baseFilters}
        values={{ search: "", status: "" }}
        defaultValues={{ search: "", status: "" }}
        onChange={vi.fn()}
      />,
    );
    expect(screen.getAllByPlaceholderText("Search...").length).toBeGreaterThan(0);
    expect(screen.getByRole("button", { name: /Status/ })).toBeInTheDocument();
  });

  it("calls onChange with the key, value and merged values when a control changes", () => {
    const onChange = vi.fn();
    render(
      <FilterToolbar<Filters>
        filters={baseFilters}
        values={{ search: "existing", status: "" }}
        defaultValues={{ search: "", status: "" }}
        onChange={onChange}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: /Status/ }));
    fireEvent.click(screen.getByText("Published"));
    expect(onChange).toHaveBeenCalledWith("status", "pub", { search: "existing", status: "pub" });
  });

  it("shows the reset button only when values differ from defaults and calls onReset with defaults", () => {
    const onReset = vi.fn();
    render(
      <FilterToolbar<Filters>
        filters={baseFilters}
        values={{ search: "", status: "pub" }}
        defaultValues={{ search: "", status: "" }}
        onChange={vi.fn()}
        onReset={onReset}
      />,
    );
    fireEvent.click(screen.getAllByRole("button", { name: /Reset/ })[0]);
    expect(onReset).toHaveBeenCalledWith({ search: "", status: "" });
  });

  it("hides the reset button when values equal defaults", () => {
    render(
      <FilterToolbar<Filters>
        filters={baseFilters}
        values={{ search: "", status: "" }}
        defaultValues={{ search: "", status: "" }}
        onChange={vi.fn()}
        onReset={vi.fn()}
      />,
    );
    expect(screen.queryByRole("button", { name: /Reset/ })).not.toBeInTheDocument();
  });
});
