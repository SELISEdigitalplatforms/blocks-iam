import type { CompiledPasswordPolicy } from "../utils/password-policy.util";
import { getStrengthColor } from "../utils/password-strength.util";
import type { PasswordChecks, PasswordRequirement } from "../utils/password-strength.util";

export type { PasswordChecks, PasswordRequirement } from "../utils/password-strength.util";
export const usePasswordStrength = (password: string, policy: CompiledPasswordPolicy | null) => {
  const checks: PasswordChecks = policy ? { policy: policy.test(password) } : {};
  const requirements: PasswordRequirement[] = policy
    ? [{ key: "policy", label: policy.message }]
    : [];
  const strength = policy ? (checks.policy ? 100 : 0) : 0;

  return {
    strength,
    checks,
    allRequirementsMet: policy ? checks.policy : password.length > 0,
    getStrengthColor: () => getStrengthColor(strength),
    requirements,
  };
};
