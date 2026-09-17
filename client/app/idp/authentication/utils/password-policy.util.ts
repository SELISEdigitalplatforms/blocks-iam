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
   * True when the tenant's rule says something the flags above cannot ("a letter, either case",
   * "one of !@#$", "no character three times running"). The rule itself is never sent: the config
   * endpoint is public, and a pattern on the wire would disclose whatever it encodes.
   *
   * The screens show one extra pass/fail row and ask `POST /api/idp/password-check` to evaluate
   * it -- the same check the account endpoints enforce on submit. This must never fall back to
   * {@link DEFAULT_PASSWORD_POLICY}: that would state requirements nobody configured.
   */
  requiresServerCheck?: boolean;
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

/** The label for a rule only the server can evaluate. */
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
  const requirements: PasswordPolicyRequirement[] = [];

  // Bounds that make no sense are "no length rule to show", not a reason to show nothing at all:
  // a server-checked rule still has its own row below.
  if (hasValidBounds(policy)) {
    requirements.push({
      key: "length",
      label: `Between ${policy.minLength} and ${policy.maxLength} characters`,
    });
  }
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
  // Last, and only when the tenant's rule says more than the flags above can. Whether it is met
  // is answered by the server; see `useServerPasswordCheck`.
  if (policy.requiresServerCheck) {
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

  // `custom` is deliberately absent here: only the server knows that answer, and the caller
  // merges it in once it arrives.
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

  // No client-side rule for a server-checked policy: the form must not pre-reject a password the
  // server would accept, and the server reports its own verdict on submit.
  return result;
};
