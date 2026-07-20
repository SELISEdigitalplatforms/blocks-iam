import { describe, expect, it } from "vitest";
import { getErrorMessage, isErrorWithErrors, handleErrorMessages } from "./error";

describe("getErrorMessage", () => {
  it("returns a fallback for empty error maps", () => {
    expect(getErrorMessage({})).toBe("Something went wrong.");
  });

  it("returns string values directly", () => {
    expect(getErrorMessage({ email: "Email is invalid" })).toEqual(["Email is invalid"]);
  });

  it("joins array values with commas", () => {
    expect(getErrorMessage({ password: ["Too short", "No symbol"] })).toEqual([
      "Too short, No symbol",
    ]);
  });

  it("prefers a mapped message when a key is in the message map", () => {
    expect(
      getErrorMessage({ user_not_found: "raw" }, { user_not_found: "No such user" }),
    ).toEqual(["No such user"]);
  });

  it("skips empty arrays and falls back when nothing usable remains", () => {
    expect(getErrorMessage({ a: [] })).toBe("Something went wrong.");
  });
});

describe("isErrorWithErrors", () => {
  it("returns true for an object with an errors object", () => {
    expect(isErrorWithErrors({ errors: { email: "bad" } })).toBe(true);
  });

  it("returns false for non-objects and objects without errors", () => {
    expect(isErrorWithErrors(null)).toBe(false);
    expect(isErrorWithErrors("oops")).toBe(false);
    expect(isErrorWithErrors({ message: "x" })).toBe(false);
  });
});

describe("handleErrorMessages", () => {
  it("returns string errors as-is", () => {
    expect(handleErrorMessages("plain error")).toBe("plain error");
  });

  it("delegates object errors to getErrorMessage", () => {
    expect(handleErrorMessages({ email: "Invalid" })).toEqual(["Invalid"]);
  });

  it("applies custom messages for object errors", () => {
    expect(handleErrorMessages({ code: "x" }, { code: "Bad code" })).toEqual(["Bad code"]);
  });

  it("returns a generic message for arrays and other shapes", () => {
    expect(handleErrorMessages(["a", "b"])).toBe("An unexpected error occurred.");
    expect(handleErrorMessages(42)).toBe("An unexpected error occurred.");
  });
});
