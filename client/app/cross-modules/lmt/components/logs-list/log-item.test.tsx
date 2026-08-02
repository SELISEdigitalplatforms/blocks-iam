import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

const navigate = vi.fn();

vi.mock("react-router", () => ({
  useNavigate: () => navigate,
}));

vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: ReactNode }) => <>{children}</>,
}));

import { LogItem } from "./log-item";
import { ILog } from "../../models/log.model";

const log: ILog = {
  timestamp: "2026-07-30T10:15:00Z",
  level: "error",
  message: "Something went wrong",
  traceId: "trace-42",
};

describe("LogItem", () => {
  beforeEach(() => navigate.mockReset());

  it("renders the level, message and trace id", () => {
    render(<LogItem log={log} />);

    expect(screen.getByText("error")).toBeInTheDocument();
    expect(screen.getByText("Something went wrong")).toBeInTheDocument();
    expect(screen.getByText("[trace-42]")).toBeInTheDocument();
  });

  it("navigates to the trace timeline when the trace id is clicked", () => {
    render(<LogItem log={log} />);

    fireEvent.click(screen.getByRole("button"));

    expect(navigate).toHaveBeenCalledWith("/tracing/timeline/trace-42");
  });

  it.each([["Enter"], [" "]])(
    "navigates to the trace timeline when %s is pressed on the trace id",
    (key) => {
      render(<LogItem log={log} />);

      fireEvent.keyDown(screen.getByRole("button"), { key });

      expect(navigate).toHaveBeenCalledWith("/tracing/timeline/trace-42");
    },
  );

  it("ignores other keys on the trace id", () => {
    render(<LogItem log={log} />);

    fireEvent.keyDown(screen.getByRole("button"), { key: "Escape" });

    expect(navigate).not.toHaveBeenCalled();
  });
});
