import { describe, expect, it } from "vitest";
import {
  firstErrorText,
  getErrorMessage,
  isErrorWithErrors,
  isNetworkError,
  handleErrorMessages,
} from "./error";

/** What genesis-os 4.5.1 throws when fetch itself fails (see `isNetworkError`). */
const networkError = (text: string) =>
  Object.assign(new Error(JSON.stringify({ general: text, message: text })), {
    status: 500,
    errors: { general: text, message: text },
  });

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
  it("returns false for a network error, so callers show their translated fallback", () => {
    expect(isErrorWithErrors(networkError("Failed to fetch"))).toBe(false);
  });
});

describe("isNetworkError", () => {
  it.each([
    "Failed to fetch",
    "NetworkError when attempting to fetch resource.",
    "Load failed",
    "Network request failed",
  ])("recognises the genesis-os transport error for %s", (text) => {
    expect(isNetworkError(networkError(text))).toBe(true);
  });

  it("recognises the shape without a status", () => {
    expect(isNetworkError({ errors: { general: "Failed to fetch", message: "Failed to fetch" } })).toBe(
      true,
    );
  });

  it("does not take a server answer for a network error", () => {
    // Another status, other fields, extra fields, or general and message that differ.
    expect(isNetworkError({ status: 400, errors: { general: "x", message: "x" } })).toBe(false);
    expect(isNetworkError({ status: 500, errors: { general: "Request failed" } })).toBe(false);
    expect(isNetworkError({ status: 500, errors: { email: "taken" } })).toBe(false);
    expect(isNetworkError({ status: 500, errors: { general: "a", message: "a", code: "c" } })).toBe(
      false,
    );
    expect(isNetworkError({ status: 500, errors: { general: "a", message: "b" } })).toBe(false);
    expect(isNetworkError({ errors: ["Failed to fetch", "Failed to fetch"] })).toBe(false);
  });

  it("returns false for plain values and errors without errors", () => {
    expect(isNetworkError(null)).toBe(false);
    expect(isNetworkError("Failed to fetch")).toBe(false);
    expect(isNetworkError(new TypeError("Failed to fetch"))).toBe(false);
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

describe("firstErrorText", () => {
  it("takes an array's first entry", () => {
    expect(firstErrorText(["first", "second"], "fallback")).toBe("first");
  });

  it("takes an object's first value, or the fallback when it has none", () => {
    expect(firstErrorText({ email: "taken", name: "short" }, "fallback")).toBe("taken");
    expect(firstErrorText({}, "fallback")).toBe("fallback");
  });

  it("returns a string as is, and the fallback for nothing", () => {
    expect(firstErrorText("plain", "fallback")).toBe("plain");
    expect(firstErrorText("", "fallback")).toBe("fallback");
    expect(firstErrorText(null, "fallback")).toBe("fallback");
  });
});
