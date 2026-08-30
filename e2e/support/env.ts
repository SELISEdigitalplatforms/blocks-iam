function stripTrailingSlash(url: string): string {
  return url.replace(/\/$/, "");
}

export function requireEnv(name: string): string {
  const value = process.env[name];
  if (!value) {
    throw new Error(`${name} is not set. Fill it in e2e/.env.e2e.`);
  }
  return value;
}

/** Blocks IAM app under test (`E2E_BASE_URL`). */
export function e2eBaseUrl(): string {
  return stripTrailingSlash(requireEnv("E2E_BASE_URL"));
}

/**
 * Derive Blocks OS origin from the IAM base URL.
 *
 * | IAM (`E2E_BASE_URL`)                         | OS (derived)                              |
 * |----------------------------------------------|-------------------------------------------|
 * | https://dev-iam.blocksdevelopers.com[:port]  | https://dev-os.blocksdevelopers.com[:port]|
 * | https://iam.seliseblocks.com                 | https://os.seliseblocks.com               |
 *
 * Override anytime with `E2E_OS_BASE_URL`.
 */
export function deriveOsBaseUrlFromIam(iamBaseUrl: string): string | undefined {
  let url: URL;
  try {
    url = new URL(iamBaseUrl);
  } catch {
    return undefined;
  }

  if (/^dev-iam\./i.test(url.hostname)) {
    url.hostname = url.hostname.replace(/^dev-iam\./i, "dev-os.");
    return stripTrailingSlash(url.origin);
  }

  if (/^iam\./i.test(url.hostname)) {
    url.hostname = url.hostname.replace(/^iam\./i, "os.");
    return stripTrailingSlash(url.origin);
  }

  return undefined;
}

/** Blocks OS — optional override; derived from IAM when unset. */
export function e2eOsBaseUrl(): string {
  const explicit = process.env.E2E_OS_BASE_URL?.trim();
  if (explicit) return stripTrailingSlash(explicit);

  const derived = deriveOsBaseUrlFromIam(e2eBaseUrl());
  if (derived) return derived;

  throw new Error(
    "E2E_OS_BASE_URL is not set and could not be derived from E2E_BASE_URL. " +
      "Examples:\n" +
      "  Dev:  E2E_BASE_URL=https://dev-iam.blocksdevelopers.com  → OS https://dev-os.blocksdevelopers.com\n" +
      "  Prod: E2E_BASE_URL=https://iam.seliseblocks.com          → OS https://os.seliseblocks.com\n" +
      "Or set E2E_OS_BASE_URL explicitly in e2e/.env.e2e.",
  );
}

export function e2eCredentials(): { email: string; password: string } {
  return {
    email: requireEnv("E2E_USERNAME"),
    password: requireEnv("E2E_PASSWORD"),
  };
}
