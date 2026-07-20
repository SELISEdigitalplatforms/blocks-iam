import { render, screen } from "@testing-library/react";
import { beforeAll, describe, expect, it, vi } from "vitest";
import { InfiniteScroll } from "./infinite-scroller";

// jsdom implements scrollTo on window but not on individual elements; the
// component scrolls its container to the bottom on mount.
beforeAll(() => {
  if (typeof Element.prototype.scrollTo !== "function") {
    Element.prototype.scrollTo = () => {};
  }
});

type Log = { id: number; text: string };

const renderItem = (item: Log) => <div key={item.id}>{item.text}</div>;

const baseProps = {
  renderItem,
  topFn: vi.fn().mockResolvedValue([] as Log[]),
  pollingFn: vi.fn().mockResolvedValue([] as Log[]),
  pollingInterval: 10_000_000,
  loadingIndicator: <div>loading-more</div>,
  bottomIndicator: (cb: () => void) => (
    <button onClick={cb}>new-data</button>
  ),
  hasTopMore: false,
};

describe("InfiniteScroll", () => {
  it("renders every item from initialData", () => {
    render(
      <InfiniteScroll<Log>
        {...baseProps}
        initialData={[
          { id: 1, text: "log alpha" },
          { id: 2, text: "log beta" },
        ]}
      />,
    );

    expect(screen.getByText("log alpha")).toBeInTheDocument();
    expect(screen.getByText("log beta")).toBeInTheDocument();
  });

  it("renders inside a scrollable container (the scroll sentinel)", () => {
    const { container } = render(
      <InfiniteScroll<Log>
        {...baseProps}
        initialData={[{ id: 1, text: "only-log" }]}
      />,
    );

    // The overflow-scroll div is the element the component observes for
    // top/bottom scroll boundaries.
    const scrollContainer = container.querySelector(".overflow-scroll");
    expect(scrollContainer).not.toBeNull();
    expect(scrollContainer).toContainElement(screen.getByText("only-log"));
  });

  it("shows the empty state when there is no data", () => {
    render(<InfiniteScroll<Log> {...baseProps} initialData={[]} />);
    expect(screen.getByText("No logs found")).toBeInTheDocument();
  });
});
