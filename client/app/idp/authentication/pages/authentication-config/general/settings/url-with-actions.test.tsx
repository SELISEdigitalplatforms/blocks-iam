import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

vi.mock("@seliseblocks/genesis-os", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@/lib/get-api-path", () => ({
  getApiUrl: (base: string, path: string) => `https://api.test/${base}/${path}`,
}));

import { UrlWithActions } from "./url-with-actions";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("UrlWithActions", () => {
  it("renders the certificate label with the composed jwks url as its title", () => {
    render(<UrlWithActions url="https://files.test/cert.pem" />);
    const label = screen.getByTitle(/well-known\/jwks\.json\?X-Blocks-Key=tenant-1/);
    expect(label).toHaveTextContent("certificate");
  });

  it("copies the jwks url to the clipboard when copy is clicked", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, "clipboard", {
      value: { writeText },
      configurable: true,
    });
    Object.defineProperty(window, "isSecureContext", { value: true, configurable: true });

    render(<UrlWithActions url="https://files.test/cert.pem" />);
    fireEvent.click(screen.getByTitle("Copy URL"));
    await waitFor(() =>
      expect(writeText).toHaveBeenCalledWith(
        expect.stringContaining("well-known/jwks.json?X-Blocks-Key=tenant-1"),
      ),
    );
  });

  it("downloads the certificate when download is clicked", async () => {
    const blob = new Blob(["cert"]);
    global.fetch = vi.fn().mockResolvedValue({ blob: () => Promise.resolve(blob) });
    const createObjectURL = vi.fn(() => "blob:url");
    const revokeObjectURL = vi.fn();
    Object.defineProperty(URL, "createObjectURL", { value: createObjectURL, configurable: true });
    Object.defineProperty(URL, "revokeObjectURL", { value: revokeObjectURL, configurable: true });

    render(<UrlWithActions url="https://files.test/cert.pem" />);
    fireEvent.click(screen.getByTitle("Download certificate"));
    await waitFor(() => expect(global.fetch).toHaveBeenCalledWith("https://files.test/cert.pem"));
    await waitFor(() => expect(createObjectURL).toHaveBeenCalledWith(blob));
    await waitFor(() => expect(revokeObjectURL).toHaveBeenCalled());
  });

  it("falls back to execCommand when the clipboard API is unavailable", async () => {
    Object.defineProperty(navigator, "clipboard", { value: undefined, configurable: true });
    Object.defineProperty(window, "isSecureContext", { value: false, configurable: true });
    const execCommand = vi.fn();
    Object.defineProperty(document, "execCommand", { value: execCommand, configurable: true });

    render(<UrlWithActions url="https://files.test/cert.pem" />);
    fireEvent.click(screen.getByTitle("Copy URL"));
    await waitFor(() => expect(execCommand).toHaveBeenCalledWith("copy"));
    // The button flips to the copied state.
    expect(screen.getByTitle("Copied!")).toBeInTheDocument();
  });

  it("logs an error when the download fails", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    render(<UrlWithActions url="https://files.test/cert.pem" />);
    fireEvent.click(screen.getByTitle("Download certificate"));
    await waitFor(() => expect(errorSpy).toHaveBeenCalled());
    errorSpy.mockRestore();
  });
});
