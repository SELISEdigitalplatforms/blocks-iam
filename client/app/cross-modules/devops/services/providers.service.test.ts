import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  authenticateWithGithub,
  verifyOAuthState,
  authenticateWithGitlab,
  authenticateWithBitbucket,
  authenticateWithAzure,
  authenticateWithAws,
} from "./providers.service";

// Shim crypto.getRandomValues if the environment does not provide it, so
// generateRandomState() works deterministically under jsdom.
if (
  typeof globalThis.crypto === "undefined" ||
  typeof globalThis.crypto.getRandomValues !== "function"
) {
  (globalThis as unknown as { crypto: Crypto }).crypto = {
    getRandomValues: <T extends ArrayBufferView | null>(a: T): T => {
      if (a) {
        const view = a as unknown as Uint8Array;
        for (let i = 0; i < view.length; i++) view[i] = i % 256;
      }
      return a;
    },
  } as unknown as Crypto;
}

describe("providers.service", () => {
  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  // ─── authenticateWithGithub ─────────────────────────────────────────────────
  describe("authenticateWithGithub", () => {
    it("should store auth state and open the GitHub OAuth URL", () => {
      const openSpy = vi.spyOn(window, "open").mockImplementation(() => null);

      authenticateWithGithub();

      // localStorage side-effects
      expect(localStorage.getItem("github_auth_state")).toBeTruthy();
      expect(localStorage.getItem("github_auth_destination")).toBe("/");
      expect(localStorage.getItem("github_auth_project_key")).toBeNull();

      // window.open called with a github authorize URL
      expect(openSpy).toHaveBeenCalledTimes(1);
      const [url, target, features] = openSpy.mock.calls[0];
      expect(typeof url).toBe("string");
      expect(url as string).toContain("github.com/login/oauth/authorize");
      expect(url as string).toContain("scope=");
      expect(url as string).toContain("state=");
      expect(target).toBe("_blank");
      expect(features).toBe("noopener,noreferrer");

      // the persisted state matches the state query param in the opened URL
      const openedUrl = new URL(url as string);
      expect(openedUrl.searchParams.get("state")).toBe(
        localStorage.getItem("github_auth_state"),
      );
    });

    it("should use the stored destination when present", () => {
      vi.spyOn(window, "open").mockImplementation(() => null);
      localStorage.setItem("destination", "/dashboard");

      authenticateWithGithub();

      expect(localStorage.getItem("github_auth_destination")).toBe("/dashboard");
    });

    it("should persist the project key when provided", () => {
      vi.spyOn(window, "open").mockImplementation(() => null);

      authenticateWithGithub(undefined, "project-key-1");

      expect(localStorage.getItem("github_auth_project_key")).toBe("project-key-1");
    });
  });

  // ─── verifyOAuthState ───────────────────────────────────────────────────────
  describe("verifyOAuthState", () => {
    it("should return true when the received state matches the stored state", () => {
      localStorage.setItem("github_auth_state", "state-abc");

      expect(verifyOAuthState("state-abc")).toBe(true);
    });

    it("should return false when the received state does not match", () => {
      localStorage.setItem("github_auth_state", "state-abc");

      expect(verifyOAuthState("different-state")).toBe(false);
    });

    it("should return false when no state is stored and a value is received", () => {
      expect(verifyOAuthState("some-state")).toBe(false);
    });
  });

  // ─── unimplemented providers ────────────────────────────────────────────────
  describe("unimplemented provider placeholders", () => {
    it("should log a message for GitLab", () => {
      const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});

      authenticateWithGitlab();

      expect(logSpy).toHaveBeenCalledWith("GitLab authentication not yet implemented");
    });

    it("should log a message for Bitbucket", () => {
      const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});

      authenticateWithBitbucket();

      expect(logSpy).toHaveBeenCalledWith("Bitbucket authentication not yet implemented");
    });

    it("should log a message for Azure", () => {
      const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});

      authenticateWithAzure();

      expect(logSpy).toHaveBeenCalledWith("Azure DevOps authentication not yet implemented");
    });

    it("should log a message for AWS", () => {
      const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});

      authenticateWithAws();

      expect(logSpy).toHaveBeenCalledWith("AWS CodeCommit authentication not yet implemented");
    });
  });
});
