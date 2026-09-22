import { useMemo } from "react";
import type {
  IOidcPasswordPolicy,
  PasswordPolicyChecks,
  PasswordPolicyRequirement,
} from "../utils/password-policy.util";
import {
  buildPasswordPolicyRequirements,
  checkPasswordAgainstPolicy,
  resolvePasswordPolicy,
} from "../utils/password-policy.util";
import {
  getStrengthColor,
  getStrengthLabel,
  getStrengthTextColor,
  scorePasswordStrength,
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
    // No tenant policy (unconfigured, still loading, or failed to load) falls back to the FE's
    // own baseline rule rather than dropping to "no requirements at all".
    const effectivePolicy = resolvePasswordPolicy(policy);
    const requirements: PasswordPolicyRequirement[] = buildPasswordPolicyRequirements(effectivePolicy);
    const checks: PasswordPolicyChecks = checkPasswordAgainstPolicy(password, effectivePolicy);

    // requirements/checks are two views over the same policy flags (built in the same order
    // from the same source), so they always describe the same set of rows -- there is no
    // separate "whole policy" test that could disagree with "every listed row passed".
    const hasPolicy = requirements.length > 0;
    const met = requirements.filter((requirement) => checks[requirement.key]).length;
    const allRequirementsMet = hasPolicy ? met === requirements.length : password.length > 0;

    // Scored from the password itself, never from how many requirements it happens to satisfy:
    // a tenant whose rule is mostly a length bound would otherwise jump straight to 100%.
    const strength = scorePasswordStrength(password);

    return {
      strength,
      checks,
      requirements,
      allRequirementsMet,
      hasPolicy,
      getStrengthColor: () => getStrengthColor(strength),
      getStrengthTextColor: () => getStrengthTextColor(strength),
      getStrengthLabel: () => getStrengthLabel(strength),
    };
  }, [password, policy]);
