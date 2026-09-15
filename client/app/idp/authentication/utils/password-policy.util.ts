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
}

/** Independent input-hygiene cap, applied regardless of whether a policy is configured. */
export const PASSWORD_MAX_INPUT_LENGTH = 256;

export type PasswordPolicyChecks = {
  length?: boolean;
  uppercase?: boolean;
  lowercase?: boolean;
  number?: boolean;
  special?: boolean;
};

export interface PasswordPolicyRequirement {
  key: keyof PasswordPolicyChecks;
  label: string;
}

// Fixed literals written directly in this file's own source -- never constructed from the
// server response. This is the entire point of the structured redesign: no RegExp is ever
// built from network data anywhere in this feature.
const ASCII_UPPER = /[A-Z]/;
const ASCII_LOWER = /[a-z]/;
const ASCII_DIGIT = /[0-9]/;
const ASCII_SPECIAL = /[^A-Za-z0-9]/;

/** A policy is only usable once its bounds are sane; a corrupt response degrades to "no policy". */
export const hasValidBounds = (policy: IOidcPasswordPolicy): boolean =>
  Number.isFinite(policy.minLength) &&
  Number.isFinite(policy.maxLength) &&
  policy.minLength > 0 &&
  policy.maxLength >= policy.minLength;

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
  if (!policy || !hasValidBounds(policy)) return schema;

  let result = schema
    .min(policy.minLength, `Password must be at least ${policy.minLength} characters long`)
    .max(policy.maxLength, `Password must be at most ${policy.maxLength} characters long`);
  if (policy.requireUppercase) result = result.regex(ASCII_UPPER, "Must include an uppercase letter");
  if (policy.requireLowercase) result = result.regex(ASCII_LOWER, "Must include a lowercase letter");
  if (policy.requireNumbers) result = result.regex(ASCII_DIGIT, "Must include a number");
  if (policy.requireSpecialChars) result = result.regex(ASCII_SPECIAL, "Must include a special character");
  return result;
};
