import { describe, expect, it } from "vitest";
import { z } from "zod";
import {
  CUSTOM_REQUIREMENT_LABEL,
  DEFAULT_PASSWORD_POLICY,
  PASSWORD_MAX_INPUT_LENGTH,
  applyPasswordPolicyToSchema,
  buildPasswordPolicyRequirements,
  checkPasswordAgainstPolicy,
  hasValidBounds,
  resolvePasswordPolicy,
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

describe("resolvePasswordPolicy", () => {
  it("returns the tenant's own policy when it is usable", () => {
    const tenantPolicy = policy({ minLength: 12, requireSpecialChars: true });
    expect(resolvePasswordPolicy(tenantPolicy)).toBe(tenantPolicy);
  });

  it.each([null, undefined])(
    "falls back to DEFAULT_PASSWORD_POLICY for %s (no tenant policy configured)",
    (nothing) => {
      expect(resolvePasswordPolicy(nothing)).toBe(DEFAULT_PASSWORD_POLICY);
    },
  );

  it("keeps a published policy even when its bounds say nothing", () => {
    // Zero bounds mean "no readable length rule", not "no policy". The server still enforces
    // something, so the baseline must not be substituted here.
    const published = policy({ minLength: 0, maxLength: 0, hasUndescribedRules: true });
    expect(resolvePasswordPolicy(published)).toBe(published);
  });

  it("DEFAULT_PASSWORD_POLICY restores the old 8-30/upper/lower/digit/special rule", () => {
    expect(DEFAULT_PASSWORD_POLICY).toEqual({
      minLength: 8,
      maxLength: 30,
      requireUppercase: true,
      requireLowercase: true,
      requireNumbers: true,
      requireSpecialChars: true,
      message: null,
    });
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

  it("falls back to the FE default policy when there is no tenant policy", () => {
    expect(schemaFor(null).safeParse("a").success).toBe(false);
    expect(schemaFor(undefined).safeParse("a").success).toBe(false);
    expect(schemaFor(null).safeParse("Sunflower7!").success).toBe(true);
  });

  it("asserts no strength rule for a policy whose bounds say nothing", () => {
    // Undescribable rule: the server is the only authority, and it reports on submit.
    expect(schemaFor(policy({ minLength: 0, maxLength: 0, hasUndescribedRules: true }))
      .safeParse("a").success).toBe(true);
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

describe("tenant pattern (a rule the four flags cannot express)", () => {
  // "a letter, either case" -- no structured equivalent, so the server sends the pattern.
  const withPattern = (pattern: string | null) =>
    policy({ minLength: 8, maxLength: 30, pattern });

  it("adds a single pass/fail requirement, last", () => {
    const requirements = buildPasswordPolicyRequirements(
      withPattern("^(?=.*[a-zA-Z]).{8,30}$"),
    );
    expect(requirements.map((r) => r.key)).toEqual(["length", "custom"]);
    expect(requirements.at(-1)?.label).toBe(CUSTOM_REQUIREMENT_LABEL);
  });

  it("checks the password against the tenant's own pattern", () => {
    const active = withPattern("^(?=.*[a-zA-Z]).{8,30}$");
    expect(checkPasswordAgainstPolicy("12345678", active).custom).toBe(false);
    expect(checkPasswordAgainstPolicy("1234567a", active).custom).toBe(true);
  });

  it("enforces the pattern in the schema as well, so the two agree", () => {
    const schema = applyPasswordPolicyToSchema(z.string(), withPattern("^(?=.*[a-zA-Z]).{8,30}$"));
    expect(schema.safeParse("12345678").success).toBe(false);
    expect(schema.safeParse("1234567a").success).toBe(true);
  });

  it("reports the requirement with the agreed label when the pattern fails", () => {
    const schema = applyPasswordPolicyToSchema(z.string(), withPattern("^(?=.*[a-zA-Z]).{8,30}$"));
    const result = schema.safeParse("12345678");
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain(CUSTOM_REQUIREMENT_LABEL);
    }
  });

  it("shows no extra row when no pattern was sent", () => {
    expect(buildPasswordPolicyRequirements(withPattern(null)).map((r) => r.key)).toEqual(["length"]);
    expect(checkPasswordAgainstPolicy("anything", withPattern(null)).custom).toBeUndefined();
  });

  it("ignores a pattern this browser cannot compile, rather than throwing", () => {
    const broken = withPattern("^(?<broken.{8,30}$");
    expect(() => buildPasswordPolicyRequirements(broken)).not.toThrow();
    expect(buildPasswordPolicyRequirements(broken).map((r) => r.key)).toEqual(["length"]);
    expect(checkPasswordAgainstPolicy("anything", broken).custom).toBeUndefined();
    expect(applyPasswordPolicyToSchema(z.string(), broken).safeParse("Sunflower7!").success).toBe(true);
  });

  it("ignores an over-long pattern", () => {
    const tooLong = withPattern("^" + "a".repeat(600) + "$");
    expect(buildPasswordPolicyRequirements(tooLong).map((r) => r.key)).toEqual(["length"]);
  });

  it("evaluates a stateful global pattern consistently across calls", () => {
    // A /g pattern carries lastIndex between calls; the check must not alternate.
    const global = policy({ minLength: 1, maxLength: 30, pattern: "[a-z]" });
    expect(checkPasswordAgainstPolicy("abc", global).custom).toBe(true);
    expect(checkPasswordAgainstPolicy("abc", global).custom).toBe(true);
  });
});

describe("undescribable rule (server could neither decode nor publish the pattern)", () => {
  const undescribable = (overrides: Partial<IOidcPasswordPolicy> = {}) =>
    policy({ minLength: 0, maxLength: 0, hasUndescribedRules: true, ...overrides });

  it("shows no requirements at all when nothing about the rule was readable", () => {
    expect(buildPasswordPolicyRequirements(undescribable())).toEqual([]);
    expect(checkPasswordAgainstPolicy("anything", undescribable())).toEqual({});
  });

  it("still shows the length row when only that much was readable", () => {
    const withLength = undescribable({ minLength: 8, maxLength: 30 });
    expect(buildPasswordPolicyRequirements(withLength).map((r) => r.key)).toEqual(["length"]);
    expect(checkPasswordAgainstPolicy("short", withLength).length).toBe(false);
    expect(checkPasswordAgainstPolicy("longenough", withLength).length).toBe(true);
  });

  it("never substitutes the FE baseline, so no invented rule blocks submission", () => {
    // The server owns this rule and reports failure itself; the form must not pre-reject.
    const schema = applyPasswordPolicyToSchema(z.string(), undescribable());
    expect(schema.safeParse("a").success).toBe(true);
    expect(schema.safeParse("no-uppercase-or-digit").success).toBe(true);
  });

  it("still applies the readable length bounds in the schema", () => {
    const schema = applyPasswordPolicyToSchema(z.string(), undescribable({ minLength: 8, maxLength: 30 }));
    expect(schema.safeParse("short").success).toBe(false);
    expect(schema.safeParse("longenough").success).toBe(true);
  });

  it("surfaces the admin's message, the one clue the server can still give", () => {
    const withMessage = undescribable({ message: "See the intranet password guide." });
    expect(resolvePasswordPolicy(withMessage).message).toBe("See the intranet password guide.");
  });
});
