import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
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

  afterEach(() => {
    vi.useRealTimers();
  });

  it("fetches and prepends older data when scrolled to the top", async () => {
    const topFn = vi.fn().mockResolvedValue([{ id: 0, text: "older-log" }]);
    const { container } = render(
      <InfiniteScroll<Log>
        {...baseProps}
        hasTopMore
        topFn={topFn}
        initialData={[{ id: 1, text: "log alpha" }]}
      />,
    );

    const scrollContainer = container.querySelector(".overflow-scroll") as HTMLElement;
    Object.defineProperty(scrollContainer, "scrollTop", { value: 0, writable: true, configurable: true });
    fireEvent.scroll(scrollContainer);

    await waitFor(() => expect(topFn).toHaveBeenCalled());
    expect(await screen.findByText("older-log")).toBeInTheDocument();
  });

  it("stops requesting older data when the top fetch returns nothing", async () => {
    const topFn = vi.fn().mockResolvedValue([]);
    const { container } = render(
      <InfiniteScroll<Log>
        {...baseProps}
        hasTopMore
        topFn={topFn}
        initialData={[{ id: 1, text: "log alpha" }]}
      />,
    );

    const scrollContainer = container.querySelector(".overflow-scroll") as HTMLElement;
    Object.defineProperty(scrollContainer, "scrollTop", { value: 0, writable: true, configurable: true });
    fireEvent.scroll(scrollContainer);
    await waitFor(() => expect(topFn).toHaveBeenCalledTimes(1));

    // hasMore is now false, so a second scroll to the top does not refetch.
    fireEvent.scroll(scrollContainer);
    await Promise.resolve();
    expect(topFn).toHaveBeenCalledTimes(1);
  });

  it("logs an error when the top fetch rejects", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const topFn = vi.fn().mockRejectedValue(new Error("boom"));
    const { container } = render(
      <InfiniteScroll<Log>
        {...baseProps}
        hasTopMore
        topFn={topFn}
        initialData={[{ id: 1, text: "log alpha" }]}
      />,
    );

    const scrollContainer = container.querySelector(".overflow-scroll") as HTMLElement;
    Object.defineProperty(scrollContainer, "scrollTop", { value: 0, writable: true, configurable: true });
    fireEvent.scroll(scrollContainer);

    await waitFor(() => expect(errorSpy).toHaveBeenCalledWith("Error fetching older data:", expect.any(Error)));
    errorSpy.mockRestore();
  });

  it("polls for newer data and surfaces the new-data indicator", async () => {
    const pollingFn = vi
      .fn()
      .mockResolvedValueOnce([{ id: 99, text: "newer-log" }])
      .mockResolvedValue([]);
    render(
      <InfiniteScroll<Log>
        {...baseProps}
        pollingFn={pollingFn}
        pollingInterval={30}
        initialData={[{ id: 1, text: "log alpha" }]}
      />,
    );

    expect(await screen.findByText("newer-log")).toBeInTheDocument();
    expect(await screen.findByText("new-data")).toBeInTheDocument();
  });

  it("scrolls to the bottom and hides the indicator when it is clicked", async () => {
    const scrollToSpy = vi.fn();
    Element.prototype.scrollTo = scrollToSpy;
    const pollingFn = vi
      .fn()
      .mockResolvedValueOnce([{ id: 99, text: "newer-log" }])
      .mockResolvedValue([]);
    render(
      <InfiniteScroll<Log>
        {...baseProps}
        pollingFn={pollingFn}
        pollingInterval={30}
        initialData={[{ id: 1, text: "log alpha" }]}
      />,
    );

    const button = await screen.findByText("new-data");
    scrollToSpy.mockClear();
    fireEvent.click(button);
    expect(scrollToSpy).toHaveBeenCalledWith(expect.objectContaining({ behavior: "smooth" }));
    await waitFor(() => expect(screen.queryByText("new-data")).toBeNull());
  });

  it("logs an error when the polling fetch rejects", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const pollingFn = vi.fn().mockRejectedValue(new Error("poll-fail"));
    render(
      <InfiniteScroll<Log>
        {...baseProps}
        pollingFn={pollingFn}
        pollingInterval={30}
        initialData={[{ id: 1, text: "log alpha" }]}
      />,
    );

    await waitFor(() =>
      expect(errorSpy).toHaveBeenCalledWith("Error fetching newer data:", expect.any(Error)),
    );
    errorSpy.mockRestore();
  });
});
