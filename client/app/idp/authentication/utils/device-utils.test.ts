import { describe, expect, it } from "vitest";
import { normalizeUserCode, formatUserCodeForDisplay, isValidUserCode } from "./device-utils";

describe("device-utils", () => {
  describe("normalizeUserCode", () => {
    it("should strip whitespace and upper-case the code", () => {
      expect(normalizeUserCode("ab cd 1234")).toBe("ABCD1234");
      expect(normalizeUserCode("  a b  ")).toBe("AB");
    });

    it("should normalize en-dash and em-dash to a hyphen", () => {
      expect(normalizeUserCode("AB–1234")).toBe("AB-1234");
      expect(normalizeUserCode("AB—1234")).toBe("AB-1234");
    });

    it("should treat null/undefined as an empty string", () => {
      expect(normalizeUserCode(undefined as unknown as string)).toBe("");
      expect(normalizeUserCode(null as unknown as string)).toBe("");
    });
  });

  describe("formatUserCodeForDisplay", () => {
    it("should group an 8-character code as XXXX-XXXX", () => {
      expect(formatUserCodeForDisplay("abcd1234")).toBe("ABCD-1234");
      expect(formatUserCodeForDisplay("abcd-1234")).toBe("ABCD-1234");
    });

    it("should return short codes without a separator", () => {
      expect(formatUserCodeForDisplay("ab")).toBe("AB");
      expect(formatUserCodeForDisplay("abcd")).toBe("ABCD");
    });

    it("should split codes longer than four characters", () => {
      expect(formatUserCodeForDisplay("abcde")).toBe("ABCD-E");
    });
  });

  describe("isValidUserCode", () => {
    it("should accept 8-character codes with or without a hyphen", () => {
      expect(isValidUserCode("abcd1234")).toBe(true);
      expect(isValidUserCode("ABCD-1234")).toBe(true);
      expect(isValidUserCode("ab cd-12 34")).toBe(true);
    });

    it("should reject codes with the wrong length", () => {
      expect(isValidUserCode("abc-1234")).toBe(false);
      expect(isValidUserCode("abcd12345")).toBe(false);
      expect(isValidUserCode("")).toBe(false);
    });
  });
});
