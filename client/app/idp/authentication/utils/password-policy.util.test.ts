import { describe, expect, it } from "vitest";
import {
  DEFAULT_POLICY_MESSAGE,
  PASSWORD_MAX_INPUT_LENGTH,
  compilePasswordPolicy,
} from "./password-policy.util";

const compile = (regex: string, message: string | null = "Tenant message", ignoreCase = false) =>
  compilePasswordPolicy({ regex, message, ignoreCase });

const labels = (regex: string, ignoreCase = false) =>
  compile(regex, "Tenant message", ignoreCase)?.criteria.map((criterion) => criterion.label);

describe("compilePasswordPolicy", () => {
  it.each([null, undefined])("returns null for a nullish policy", (policy) => {
    expect(compilePasswordPolicy(policy)).toBeNull();
  });

  it.each(["", "   "])("returns null for a blank pattern", (regex) => {
    expect(compilePasswordPolicy({ regex, message: "Ignored", ignoreCase: true })).toBeNull();
  });

  it("catches patterns JavaScript cannot compile", () => {
    expect(
      compilePasswordPolicy({ regex: "^(?<-a>x)$", message: "Ignored", ignoreCase: true }),
    ).toBeNull();
  });

  it("preserves the pattern and ignore-case behavior", () => {
    const policy = compile("^abc$", "Use abc", true);
    expect(policy?.test("ABC")).toBe(true);
    expect(policy?.message).toBe("Use abc");
  });

  it("compiles case-sensitively when requested and supplies the fallback message", () => {
    const policy = compile("^abc$", null);
    expect(policy?.test("ABC")).toBe(false);
    expect(policy?.message).toBe(DEFAULT_POLICY_MESSAGE);
  });

  it("sets the input cap to 256 characters", () => {
    expect(PASSWORD_MAX_INPUT_LENGTH).toBe(256);
  });
});

describe("compilePasswordPolicy criteria", () => {
  it("names each lookahead of a typical complexity pattern", () => {
    expect(labels("^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d)(?=.*[^\\da-zA-Z]).{8,}$")).toEqual([
      "One lowercase letter",
      "One uppercase letter",
      "One number",
      "One special character",
      "At least 8 characters",
    ]);
  });

  it.each([
    ["^(?=.*[0-9]).{8,}$", "One number"],
    ["^(?=.*[a-zA-Z]).{8,}$", "One letter"],
    ["^(?=.*[!@#$%^&*]).{8,}$", "One special character"],
    ["^(?=.*\\W).{8,}$", "One special character"],
    ["^(?=\\D*\\d).{8,}$", "One number"],
    ["^(?=(.*[A-Z]){2,}).{8,}$", "At least 2 uppercase letters"],
    ["^(?=.*[A-Z]{2}).{8,}$", "At least 2 uppercase letters"],
  ])("describes %s", (regex, expected) => {
    expect(labels(regex)?.[0]).toBe(expected);
  });

  it.each([
    ["^.{8,}$", "At least 8 characters"],
    ["^.{8,64}$", "Between 8 and 64 characters"],
    ["^.{12}$", "Exactly 12 characters"],
    ["^[\\s\\S]{8,}$", "At least 8 characters"],
  ])("describes the length rule of %s", (regex, expected) => {
    expect(labels(regex)).toEqual([expected]);
  });

  it("tests each rule independently", () => {
    const policy = compile("^(?=.*[A-Z])(?=.*\\d).{8,}$");
    const results = policy!.criteria.map((criterion) => criterion.test("Sunflower"));
    expect(results).toEqual([true, false, true]);
  });

  it("keeps the whole pattern's ignore-case behaviour in every rule", () => {
    const sensitive = compile("^(?=.*[A-Z]).{4,}$");
    const insensitive = compile("^(?=.*[A-Z]).{4,}$", "Tenant message", true);
    expect(sensitive!.criteria[0].test("sunflower")).toBe(false);
    expect(insensitive!.criteria[0].test("sunflower")).toBe(true);
  });

  it.each([
    "^\\w+@\\w+$",
    "(?=.*\\d).{8,}",
    "^(?=.*[a-z])(?!.*(.)\\1).{8,}$",
    "^(?=.*\\d)?.{8,}$",
    "^(?=.*[a-z])abc.{8,}$",
  ])("falls back to the whole pattern for %s", (regex) => {
    const policy = compile(regex, "Tenant message");
    expect(policy?.criteria).toHaveLength(1);
    expect(policy?.criteria[0]).toMatchObject({ key: "policy", label: "Tenant message" });
  });

  it("keeps the rules equivalent to the whole pattern", () => {
    const policy = compile("^(?=.*[a-z])(?=.*[A-Z])(?=.*\\d).{8,16}$")!;
    const samples = [
      "",
      "short",
      "alllowercase",
      "ALLUPPERCASE1",
      "Sunflower7",
      "Sunflower",
      "Sunflower7Sunflower7",
      "Ab1",
    ];
    for (const sample of samples) {
      const everyRuleMet = policy.criteria.every((criterion) => criterion.test(sample));
      expect(everyRuleMet, sample).toBe(policy.test(sample));
    }
  });
});
