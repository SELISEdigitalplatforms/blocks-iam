export interface IOidcPasswordPolicy {
  regex: string;
  message: string | null;
  ignoreCase: boolean;
}

export interface CompiledPasswordPolicy {
  test: (password: string) => boolean;
  message: string;
}

export const PASSWORD_MAX_INPUT_LENGTH = 256;

export const DEFAULT_POLICY_MESSAGE =
  "Password does not meet this organisation's requirements.";

export const compilePasswordPolicy = (
  policy: IOidcPasswordPolicy | null | undefined,
): CompiledPasswordPolicy | null => {
  if (!policy || !policy.regex.trim()) return null;

  try {
    const regex = new RegExp(policy.regex, policy.ignoreCase ? "i" : "");
    return {
      test: (password: string) => regex.test(password),
      message: policy.message ?? DEFAULT_POLICY_MESSAGE,
    };
  } catch {
    return null;
  }
};
