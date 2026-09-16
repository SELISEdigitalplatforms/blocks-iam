import { z } from "zod";

/**
 * The tenant's structured password rule, as published by `GET /api/idp/oidc-ui-config`
 * (server-side: SPEC16). Plain data -- no function, no RegExp, and no compile step exists for
 * this type. `message` is optional admin-authored guidance, never independently enforced.
 */
export interface IOidcPasswordPolicy {
  minLength: number;
  maxLength: number;
  requireUppercase: boolean;
  requireLowercase: boolean;
  requireNumbers: boolean;
  requireSpecialChars: boolean;
  message: string | null;
  /**
   * The tenant's own pattern, sent only when their rule says something the four flags cannot
   * ("a letter, either case", "one of !@#$", "no character three times running"). Shown as a
   * single pass/fail row rather than silently dropping to a rule nobody configured.
   *
   * The server publishes this only after `PasswordPolicyRegexValidator` has screened it at save
   * time for JavaScript compatibility and catastrophic backtracking. It is still treated as
   * untrusted here: compiled behind try/catch, length-capped, and only ever run against input
   * already bounded by {@link PASSWORD_MAX_INPUT_LENGTH}.
   */
  pattern?: string | null;
  /**
   * True when the tenant's rule could be neither decoded into the flags above nor safely sent as
   * {@link pattern} -- a pattern the server's save-time screening refuses to publish.
   *
   * The rule is still enforced on submit, so this must NOT fall back to
   * {@link DEFAULT_PASSWORD_POLICY}: that would state requirements nobody configured. The screens
   * show whatever the other fields do say (the length bounds when readable, nothing when not)
   * and let the server's own error report the failure.
   */
  hasUndescribedRules?: boolean;
}

/** Independent input-hygiene cap, applied regardless of whether a policy is configured. */
export const PASSWORD_MAX_INPUT_LENGTH = 256;

export type PasswordPolicyChecks = {
  length?: boolean;
  uppercase?: boolean;
  lowercase?: boolean;
  number?: boolean;
  special?: boolean;
  custom?: boolean;
};

export interface PasswordPolicyRequirement {
  key: keyof PasswordPolicyChecks;
  label: string;
}

// Fixed literals written directly in this file's own source. Every rule a tenant can express
// with the four flags is checked with these and nothing else -- the common case builds no
// RegExp from the server response at all.
const ASCII_UPPER = /[A-Z]/;
const ASCII_LOWER = /[a-z]/;
const ASCII_DIGIT = /[0-9]/;
const ASCII_SPECIAL = /[^A-Za-z0-9]/;

/** The longest pattern accepted from the server, matching the server's own save-time cap. */
const MAX_PATTERN_LENGTH = 512;

/**
 * Compiles the tenant's pattern, or returns null if it will not compile here. A pattern the
 * server screened can still be rejected by this browser's engine, and that must degrade to
 * "no extra requirement shown" rather than throwing inside a render.
 */
const compiledPatterns = new Map<string, RegExp | null>();

export const compilePolicyPattern = (pattern: string | null | undefined): RegExp | null => {
  if (!pattern || pattern.length > MAX_PATTERN_LENGTH) return null;

  const cached = compiledPatterns.get(pattern);
  if (cached !== undefined) return cached;

  let compiled: RegExp | null = null;
  try {
    compiled = new RegExp(pattern);
  } catch {
    compiled = null;
  }
  compiledPatterns.set(pattern, compiled);
  return compiled;
};

/** The label for a rule only the tenant's own pattern can express. */
export const CUSTOM_REQUIREMENT_LABEL = "Meets your project's password requirements";

/** A policy is only usable once its bounds are sane; a corrupt response degrades to the default. */
export const hasValidBounds = (policy: IOidcPasswordPolicy): boolean =>
  Number.isFinite(policy.minLength) &&
  Number.isFinite(policy.maxLength) &&
  policy.minLength > 0 &&
  policy.maxLength >= policy.minLength;

/**
 * The FE's baseline password rule -- restores the pre-tenant-configurable behaviour that was
 * hard-coded everywhere before SPEC16/17 (8-30 characters, upper, lower, digit, special/`_`;
 * `_` falls under the fixed ASCII_SPECIAL class below like it always has).
 *
 * Applied whenever a tenant has no structured policy configured (or the config hasn't loaded
 * yet/failed to load), so the screens never drop to "no requirements at all" -- by decision,
 * every tenant gets at least this floor.
 */
export const DEFAULT_PASSWORD_POLICY: IOidcPasswordPolicy = {
  minLength: 8,
  maxLength: 30,
  requireUppercase: true,
  requireLowercase: true,
  requireNumbers: true,
  requireSpecialChars: true,
  message: null,
};

/**
 * The policy actually in effect: the tenant's own when one was published, else the FE's baseline.
 *
 * The baseline applies only when the server published no policy at all -- no rule is configured,
 * or the config has not loaded. A policy that *was* published is always used as-is, even when it
 * describes little or nothing: the server has a real rule in that case, and substituting the
 * baseline would show the user requirements it does not enforce.
 */
export const resolvePasswordPolicy = (
  policy: IOidcPasswordPolicy | null | undefined,
): IOidcPasswordPolicy => policy ?? DEFAULT_PASSWORD_POLICY;

export const buildPasswordPolicyRequirements = (
  policy: IOidcPasswordPolicy,
): PasswordPolicyRequirement[] => {
  if (!hasValidBounds(policy)) return [];

  const requirements: PasswordPolicyRequirement[] = [
    { key: "length", label: `Between ${policy.minLength} and ${policy.maxLength} characters` },
  ];
  if (policy.requireUppercase) {
    requirements.push({ key: "uppercase", label: "At least one uppercase letter (A-Z)" });
  }
  if (policy.requireLowercase) {
    requirements.push({ key: "lowercase", label: "At least one lowercase letter (a-z)" });
  }
  if (policy.requireNumbers) {
    requirements.push({ key: "number", label: "At least one number (0-9)" });
  }
  if (policy.requireSpecialChars) {
    requirements.push({ key: "special", label: "At least one special character" });
  }
  // Last, and only when the tenant's rule says more than the flags above can.
  if (compilePolicyPattern(policy.pattern)) {
    requirements.push({ key: "custom", label: CUSTOM_REQUIREMENT_LABEL });
  }
  return requirements;
};

export const checkPasswordAgainstPolicy = (
  password: string,
  policy: IOidcPasswordPolicy,
): PasswordPolicyChecks => {
  if (!hasValidBounds(policy)) return {};

  const checks: PasswordPolicyChecks = {
    length: password.length >= policy.minLength && password.length <= policy.maxLength,
  };
  if (policy.requireUppercase) checks.uppercase = ASCII_UPPER.test(password);
  if (policy.requireLowercase) checks.lowercase = ASCII_LOWER.test(password);
  if (policy.requireNumbers) checks.number = ASCII_DIGIT.test(password);
  if (policy.requireSpecialChars) checks.special = ASCII_SPECIAL.test(password);

  const compiled = compilePolicyPattern(policy.pattern);
  if (compiled) {
    // Bounded input only: the field caps at PASSWORD_MAX_INPUT_LENGTH, and the server screened
    // the pattern for catastrophic backtracking before publishing it.
    compiled.lastIndex = 0;
    checks.custom = compiled.test(password.slice(0, PASSWORD_MAX_INPUT_LENGTH));
  }
  return checks;
};

/**
 * Applies the same active policy to a zod string schema as the on-screen checker applies via
 * {@link checkPasswordAgainstPolicy}, so the two can never disagree (H6). Every `.regex(...)`
 * call uses a fixed literal pattern written directly in this file's own source -- never a
 * pattern read from `policy` or any network response.
 */
export const applyPasswordPolicyToSchema = (
  schema: z.ZodString,
  policy: IOidcPasswordPolicy | null | undefined,
): z.ZodString => {
  const resolved = resolvePasswordPolicy(policy);

  // Bounds that make no sense are "no length rule to state", not a rule rejecting everything.
  let result = schema;
  if (hasValidBounds(resolved)) {
    result = result
      .min(resolved.minLength, `Password must be at least ${resolved.minLength} characters long`)
      .max(resolved.maxLength, `Password must be at most ${resolved.maxLength} characters long`);
  }
  if (resolved.requireUppercase) result = result.regex(ASCII_UPPER, "Must include an uppercase letter");
  if (resolved.requireLowercase) result = result.regex(ASCII_LOWER, "Must include a lowercase letter");
  if (resolved.requireNumbers) result = result.regex(ASCII_DIGIT, "Must include a number");
  if (resolved.requireSpecialChars) result = result.regex(ASCII_SPECIAL, "Must include a special character");

  const compiled = compilePolicyPattern(resolved.pattern);
  if (compiled) result = result.regex(compiled, CUSTOM_REQUIREMENT_LABEL);
  return result;
};
