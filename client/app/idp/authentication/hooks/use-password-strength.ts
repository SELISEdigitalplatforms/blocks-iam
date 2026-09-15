import { useMemo } from "react";
import type {
  IOidcPasswordPolicy,
  PasswordPolicyChecks,
  PasswordPolicyRequirement,
} from "../utils/password-policy.util";
import { buildPasswordPolicyRequirements, checkPasswordAgainstPolicy } from "../utils/password-policy.util";
import {
  STRENGTH_THRESHOLDS,
  getStrengthColor,
  getStrengthLabel,
  getStrengthTextColor,
} from "../utils/password-strength.util";

export type {
  PasswordPolicyChecks,
  PasswordPolicyRequirement,
} from "../utils/password-policy.util";

export const usePasswordStrength = (
  password: string,
  policy: IOidcPasswordPolicy | null | undefined,
) =>
  useMemo(() => {
    const requirements: PasswordPolicyRequirement[] = policy ? buildPasswordPolicyRequirements(policy) : [];
    const checks: PasswordPolicyChecks = policy ? checkPasswordAgainstPolicy(password, policy) : {};

    // requirements/checks are two views over the same policy flags (built in the same order
    // from the same source), so they always describe the same set of rows -- there is no
    // separate "whole policy" test that could disagree with "every listed row passed".
    const hasPolicy = requirements.length > 0;
    const met = requirements.filter((requirement) => checks[requirement.key]).length;
    const allRequirementsMet = hasPolicy ? met === requirements.length : password.length > 0;

    const strength =
      !hasPolicy || password === ""
        ? 0
        : met === requirements.length
          ? 100
          : Math.min(Math.round((met / requirements.length) * 100), STRENGTH_THRESHOLDS.STRONG);

    return {
      strength,
      checks,
      requirements,
      allRequirementsMet,
      hasPolicy,
      policyMessage: policy?.message ?? null,
      getStrengthColor: () => getStrengthColor(strength),
      getStrengthTextColor: () => getStrengthTextColor(strength),
      getStrengthLabel: () => getStrengthLabel(strength),
    };
  }, [password, policy]);
