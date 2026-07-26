import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ showErrorToast: vi.fn() }));

vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: (key: string) =>
    ({
      BLOCKS_X_BLOCKS_KEY: "bk",
      BLOCKS_IAM_BASE_URL: "https://iam.test",
      BLOCKS_OS_CLIENT_ID: "os-client",
      BLOCKS_OS_CALLBACK_URL: "https://os.test/cb",
    })[key] || "",
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: h.showErrorToast }));

import ConsoleCreateProject from "./console-create";

const originalLocation = window.location;

beforeEach(() => {
  vi.clearAllMocks();
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { href: "" },
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: originalLocation,
  });
});

describe("ConsoleCreateProject", () => {
  it("renders the welcome card and create button", () => {
    render(<ConsoleCreateProject />);
    expect(screen.getByText("Welcome to SELISE Blocks")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create a project" })).toBeInTheDocument();
  });

  it("redirects to the authorization URL returned by the initiate call", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      json: async () => ({ redirect_uri: "https://iam.test/authorize" }),
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<ConsoleCreateProject />);
    fireEvent.click(screen.getByRole("button", { name: "Create a project" }));

    await waitFor(() => expect(window.location.href).toBe("https://iam.test/authorize"));
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toContain("/api/idp/initiate");
    expect(options.headers["X-Blocks-Key"]).toBe("bk");
  });

  it("shows an error toast when no authorization URL is returned", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ json: async () => ({}) });
    vi.stubGlobal("fetch", fetchMock);

    render(<ConsoleCreateProject />);
    fireEvent.click(screen.getByRole("button", { name: "Create a project" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Failed to get authorization URL" }),
    );
  });

  it("shows an error toast when the initiate call throws", async () => {
    const errorSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const fetchMock = vi.fn().mockRejectedValue(new Error("network"));
    vi.stubGlobal("fetch", fetchMock);

    render(<ConsoleCreateProject />);
    fireEvent.click(screen.getByRole("button", { name: "Create a project" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "Unable to open Blocks OS. Please try again.",
      }),
    );
    errorSpy.mockRestore();
  });
});
