import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CopyableSnippet } from "./copyable-snippet";

describe("CopyableSnippet", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the code and copies it to the clipboard", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });

    render(<CopyableSnippet code="npm install" language="bash" isCopyable />);
    // SyntaxHighlighter tokenises the code across many spans, so assert on the
    // copy affordance rather than the code text.
    expect(screen.getByLabelText("Copy code")).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText("Copy code"));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith("npm install"));
  });

  it("hides the copy button when not copyable", () => {
    render(<CopyableSnippet code="ls" isCopyable={false} />);
    expect(screen.queryByLabelText("Copy code")).not.toBeInTheDocument();
  });

  it("falls back to execCommand when the clipboard API is unavailable", async () => {
    Object.assign(navigator, { clipboard: undefined });
    const exec = vi.fn();
    (document as unknown as { execCommand: unknown }).execCommand = exec;

    render(<CopyableSnippet code="echo hi" isCopyable />);
    fireEvent.click(screen.getByLabelText("Copy code"));
    await waitFor(() => expect(exec).toHaveBeenCalledWith("copy"));
  });
});
