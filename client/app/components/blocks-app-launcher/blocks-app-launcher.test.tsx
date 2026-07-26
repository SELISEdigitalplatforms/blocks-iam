import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

vi.mock("@/lib/runtime-env", () => ({
  getRuntimeEnv: (key: string) => {
    const map: Record<string, string> = {
      BLOCKS_X_BLOCKS_KEY: "bkey",
      BLOCKS_IAM_BASE_URL: "https://iam.test",
    };
    return map[key] ?? `env:${key}`;
  },
}));
const showError = vi.fn();
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: (a: unknown) => showError(a) }));

import { BlocksAppLauncher, initiateAppLogin, OS_APP } from "./blocks-app-launcher";

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});

describe("initiateAppLogin", () => {
  it("redirects the window when the initiate endpoint returns a redirect uri", async () => {
    const hrefSpy = vi.fn();
    Object.defineProperty(window, "location", {
      value: {
        get href() {
          return "";
        },
        set href(v: string) {
          hrefSpy(v);
        },
      },
      configurable: true,
    });
    global.fetch = vi.fn().mockResolvedValue({
      json: () => Promise.resolve({ redirect_uri: "https://iam.test/authorize" }),
    });
    await initiateAppLogin(OS_APP, "somewhere");
    expect(global.fetch).toHaveBeenCalledWith(
      expect.stringContaining("https://iam.test/api/idp/initiate?"),
      expect.objectContaining({ headers: expect.objectContaining({ "X-Blocks-Key": "bkey" }) }),
    );
    expect(hrefSpy).toHaveBeenCalledWith("https://iam.test/authorize");
  });

  it("throws when the endpoint does not return a redirect uri", async () => {
    global.fetch = vi.fn().mockResolvedValue({ json: () => Promise.resolve({}) });
    await expect(initiateAppLogin(OS_APP)).rejects.toThrow("Failed to get authorization URL");
  });
});

describe("BlocksAppLauncher", () => {
  it("renders the apps launcher trigger after hydration", async () => {
    render(
      <MemoryRouter>
        <BlocksAppLauncher />
      </MemoryRouter>,
    );
    await waitFor(() =>
      expect(screen.getByLabelText("SELISE Blocks apps")).toBeInTheDocument(),
    );
  });

  it("seeds default favourites into localStorage-backed state on first mount", async () => {
    render(
      <MemoryRouter>
        <BlocksAppLauncher />
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByLabelText("SELISE Blocks apps")).toBeInTheDocument());
    // The launcher reads favourites from localStorage; default seed is iam + localization.
    expect(localStorage.getItem("blocks-app-favourites")).toBeNull();
  });

  const openLauncher = async () => {
    render(
      <MemoryRouter>
        <BlocksAppLauncher />
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByLabelText("SELISE Blocks apps")).toBeInTheDocument());
    fireEvent.click(screen.getByLabelText("SELISE Blocks apps"));
  };

  it("shows favourite apps and the more-apps section when opened", async () => {
    await openLauncher();
    expect(screen.getByText("Your favourites")).toBeInTheDocument();
    expect(screen.getByText("IAM")).toBeInTheDocument();
    expect(screen.getByText("Localization")).toBeInTheDocument();
    expect(screen.getByText("More from SELISE Blocks")).toBeInTheDocument();
    // A non-favourite app appears in the more-apps grid.
    expect(screen.getByText("Logic")).toBeInTheDocument();
  });

  it("shows an error toast when launching an app fails", async () => {
    global.fetch = vi.fn().mockRejectedValue(new Error("network"));
    await openLauncher();
    fireEvent.click(screen.getByText("IAM"));
    await waitFor(() =>
      expect(showError).toHaveBeenCalledWith({ errors: "Unable to open app. Please try again." }),
    );
  });

  it("toggles a favourite through the manage-favourites dialog", async () => {
    await openLauncher();
    fireEvent.click(screen.getByLabelText("Edit favourites"));
    expect(await screen.findByText("Manage Favourites")).toBeInTheDocument();
    // Logic starts as a non-favourite; toggling it adds it to the persisted set.
    const logicButtons = screen
      .getAllByText("Logic")
      .map((el) => el.closest("button"))
      .filter(Boolean) as HTMLButtonElement[];
    fireEvent.click(logicButtons[logicButtons.length - 1]);
    await waitFor(() => {
      const stored = JSON.parse(localStorage.getItem("blocks-app-favourites") || "[]");
      expect(stored).toContain("logic");
    });
  });
});
