import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CopyToClipboardButton } from "./copy-to-clipboard-button";

describe("CopyToClipboardButton", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    Object.defineProperty(window, "isSecureContext", { value: true, configurable: true });
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("copies text using the clipboard API in a secure context", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.assign(navigator, { clipboard: { writeText } });

    render(
      <CopyToClipboardButton textToCopy="secret-value">
        <span>Key</span>
      </CopyToClipboardButton>,
    );
    expect(screen.getByText("Key")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(writeText).toHaveBeenCalledWith("secret-value"));
  });

  it("uses the execCommand fallback outside a secure context", async () => {
    Object.defineProperty(window, "isSecureContext", { value: false, configurable: true });
    const exec = vi.fn();
    (document as unknown as { execCommand: unknown }).execCommand = exec;

    render(
      <CopyToClipboardButton textToCopy="fallback-value" isHoverable>
        <span>Token</span>
      </CopyToClipboardButton>,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(exec).toHaveBeenCalledWith("copy"));
  });
});
