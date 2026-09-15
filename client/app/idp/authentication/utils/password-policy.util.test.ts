import { describe, expect, it } from "vitest";
import { z } from "zod";
import {
  PASSWORD_MAX_INPUT_LENGTH,
  applyPasswordPolicyToSchema,
  buildPasswordPolicyRequirements,
  checkPasswordAgainstPolicy,
  hasValidBounds,
  type IOidcPasswordPolicy,
} from "./password-policy.util";

const policy = (overrides: Partial<IOidcPasswordPolicy> = {}): IOidcPasswordPolicy => ({
  minLength: 8,
  maxLength: 64,
  requireUppercase: false,
  requireLowercase: false,
  requireNumbers: false,
  requireSpecialChars: false,
  message: null,
  ...overrides,
});

describe("PASSWORD_MAX_INPUT_LENGTH", () => {
  it("is 256", () => {
    expect(PASSWORD_MAX_INPUT_LENGTH).toBe(256);
  });
});

describe("hasValidBounds", () => {
  it.each([
    [policy({ minLength: 8, maxLength: 64 }), true],
    [policy({ minLength: 8, maxLength: 8 }), true],
    [policy({ minLength: 0, maxLength: 64 }), false],
    [policy({ minLength: -1, maxLength: 64 }), false],
    [policy({ minLength: 20, maxLength: 12 }), false],
    [policy({ minLength: Number.NaN, maxLength: 64 }), false],
  ])("reports %j as %s", (p, expected) => {
    expect(hasValidBounds(p)).toBe(expected);
  });
});

describe("buildPasswordPolicyRequirements", () => {
  it("always includes the length row, labelled from the policy's own bounds", () => {
    expect(buildPasswordPolicyRequirements(policy({ minLength: 10, maxLength: 64 }))).toEqual([
      { key: "length", label: "Between 10 and 64 characters" },
    ]);
  });

  it("adds one row per true requireXxx flag, in a fixed order", () => {
    const requirements = buildPasswordPolicyRequirements(
      policy({
        requireUppercase: true,
        requireLowercase: true,
        requireNumbers: true,
        requireSpecialChars: true,
      }),
    );
    expect(requirements.map((r) => r.key)).toEqual([
      "length",
      "uppercase",
      "lowercase",
      "number",
      "special",
    ]);
    expect(requirements.map((r) => r.label)).toEqual([
      "Between 8 and 64 characters",
      "At least one uppercase letter (A-Z)",
      "At least one lowercase letter (a-z)",
      "At least one number (0-9)",
      "At least one special character",
    ]);
  });

  it("omits rows for flags that are false", () => {
    const requirements = buildPasswordPolicyRequirements(policy({ requireNumbers: true }));
    expect(requirements.map((r) => r.key)).toEqual(["length", "number"]);
  });

  it("C1: returns no rows at all for a policy with invalid bounds, rather than throwing", () => {
    expect(() => buildPasswordPolicyRequirements(policy({ minLength: 0 }))).not.toThrow();
    expect(buildPasswordPolicyRequirements(policy({ minLength: 0 }))).toEqual([]);
  });
});

describe("checkPasswordAgainstPolicy", () => {
  it("checks and requirements always describe the same set of rows (D3)", () => {
    const p = policy({ requireUppercase: true, requireNumbers: true });
    const requirements = buildPasswordPolicyRequirements(p);
    const checks = checkPasswordAgainstPolicy("Sunflower7", p);
    expect(Object.keys(checks).sort()).toEqual(requirements.map((r) => r.key).sort());
  });

  it("evaluates length against the policy's own bounds", () => {
    const p = policy({ minLength: 10, maxLength: 12 });
    expect(checkPasswordAgainstPolicy("short", p).length).toBe(false);
    expect(checkPasswordAgainstPolicy("justright12", p).length).toBe(true);
    expect(checkPasswordAgainstPolicy("waytoolongforthis", p).length).toBe(false);
  });

  it.each([
    ["requireUppercase" as const, "uppercase" as const, "PASSWORD", "password"],
    ["requireLowercase" as const, "lowercase" as const, "password", "PASSWORD"],
    ["requireNumbers" as const, "number" as const, "password1", "password"],
    ["requireSpecialChars" as const, "special" as const, "password!", "password1"],
  ])("evaluates %s using a fixed ASCII class check", (flag, key, passes, fails) => {
    const p = policy({ [flag]: true } as Partial<IOidcPasswordPolicy>);
    expect(checkPasswordAgainstPolicy(passes, p)[key]).toBe(true);
    expect(checkPasswordAgainstPolicy(fails, p)[key]).toBe(false);
  });

  it("treats a non-ASCII letter as neither uppercase, lowercase, nor a number -- only special", () => {
    const p = policy({
      requireUppercase: true,
      requireLowercase: true,
      requireNumbers: true,
      requireSpecialChars: true,
    });
    const checks = checkPasswordAgainstPolicy("café", p);
    expect(checks.uppercase).toBe(false);
    expect(checks.lowercase).toBe(true); // "caf" supplies an ASCII lowercase letter
    expect(checks.number).toBe(false);
    expect(checks.special).toBe(true); // "é" is not A-Za-z0-9
  });

  it("C1: returns no checks at all for a policy with invalid bounds, rather than throwing", () => {
    expect(() => checkPasswordAgainstPolicy("anything", policy({ maxLength: 0 }))).not.toThrow();
    expect(checkPasswordAgainstPolicy("anything", policy({ maxLength: 0 }))).toEqual({});
  });
});

describe("applyPasswordPolicyToSchema", () => {
  const schemaFor = (p: IOidcPasswordPolicy | null | undefined) =>
    applyPasswordPolicyToSchema(z.string(), p);

  it("returns the schema unchanged when there is no policy", () => {
    expect(schemaFor(null).safeParse("a").success).toBe(true);
    expect(schemaFor(undefined).safeParse("a").success).toBe(true);
  });

  it("returns the schema unchanged for a policy with invalid bounds", () => {
    expect(schemaFor(policy({ minLength: 0 })).safeParse("a").success).toBe(true);
  });

  it("enforces the policy's own length bounds with matching messages", () => {
    const schema = schemaFor(policy({ minLength: 10, maxLength: 12 }));
    const tooShort = schema.safeParse("short");
    expect(tooShort.success).toBe(false);
    if (!tooShort.success) {
      expect(tooShort.error.issues[0]?.message).toBe(
        "Password must be at least 10 characters long",
      );
    }

    const tooLong = schema.safeParse("waytoolongforthis");
    expect(tooLong.success).toBe(false);
    if (!tooLong.success) {
      expect(tooLong.error.issues[0]?.message).toBe(
        "Password must be at most 12 characters long",
      );
    }

    expect(schema.safeParse("justright12").success).toBe(true);
  });

  it("adds one regex clause per active flag, using fixed literal patterns", () => {
    const schema = schemaFor(
      policy({
        minLength: 1,
        maxLength: 64,
        requireUppercase: true,
        requireLowercase: true,
        requireNumbers: true,
        requireSpecialChars: true,
      }),
    );
    expect(schema.safeParse("Sunflower7!").success).toBe(true);

    const missingUppercase = schema.safeParse("sunflower7!");
    expect(missingUppercase.success).toBe(false);
    if (!missingUppercase.success) {
      expect(missingUppercase.error.issues.map((i) => i.message)).toContain(
        "Must include an uppercase letter",
      );
    }
  });

  it("matches checkPasswordAgainstPolicy's pass/fail exactly (H6)", () => {
    const p = policy({
      minLength: 8,
      maxLength: 20,
      requireUppercase: true,
      requireNumbers: true,
    });
    const schema = schemaFor(p);
    for (const candidate of ["short", "sunflower", "Sunflower", "Sunflower7", "SUNFLOWER7"]) {
      const checks = checkPasswordAgainstPolicy(candidate, p);
      const allChecksPass = Object.values(checks).every(Boolean);
      expect(schema.safeParse(candidate).success, candidate).toBe(allChecksPass);
    }
  });
});
