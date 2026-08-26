import { beforeEach, describe, expect, it, vi } from "vitest";
import { getSelfBaseUrl } from "@/lib/runtime-env";
import { getApiPath, getApiUrl } from "./get-api-path";

vi.mock("@/lib/runtime-env", () => ({ getSelfBaseUrl: vi.fn(() => "https://api.example.com") }));

describe("get-api-path", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getSelfBaseUrl).mockReturnValue("https://api.example.com");
  });

  describe("getApiPath", () => {
    it("should always return the /api prefix regardless of the legacy service path", () => {
      expect(getApiPath("iam")).toBe("/api");
      expect(getApiPath("")).toBe("/api");
      expect(getApiPath("anything")).toBe("/api");
    });
  });

  describe("getApiUrl", () => {
    it("should build base origin + /api + endpoint", () => {
      expect(getApiUrl("iam", "Authentication/Login")).toBe(
        "https://api.example.com/api/Authentication/Login",
      );
    });

    it("should read the base URL from the serving origin, not the injected config", () => {
      expect(getApiUrl("iam", "Authentication/Login")).toBe(
        "https://api.example.com/api/Authentication/Login",
      );
      expect(getSelfBaseUrl).toHaveBeenCalled();
    });

    it("should handle an empty base URL", () => {
      vi.mocked(getSelfBaseUrl).mockReturnValue("");
      expect(getApiUrl("iam", ".well-known/jwks.json")).toBe("/api/.well-known/jwks.json");
    });
  });
});
