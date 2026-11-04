import { expect, type Page } from "@playwright/test";
import { e2eBaseUrl, e2eCredentials } from "./env";
import { iamApi } from "./iam-api";

/**
 * Helpers that keep the shared E2E account usable for every suite that signs in with it
 * (blocks-iam, blocks-os, and others).
 *
 * Two things break that account for everyone else:
 * - a spec changes its password and never changes it back;
 * - two failed logins in a row, after which IAM asks for a CAPTCHA on every login.
 *
 * A successful change-password call fixes both: the backend stores the new password and
 * clears the failed-login counter. It only needs the signed-in session, not a login, so it
 * still works once the CAPTCHA gate is on.
 */

const CHANGE_PASSWORD_PATH = "/api/auth/change-password";

type ChangePasswordResult = { isSuccess?: boolean };

async function onSignedInPage(page: Page): Promise<void> {
  const base = e2eBaseUrl();
  if (!page.url().startsWith(base) || !/\/app\//.test(new URL(page.url()).pathname)) {
    await page.goto(`${base}/app/profile`, { waitUntil: "domcontentloaded" });
  }
  await expect(page).toHaveURL(/\/app\//, { timeout: 30_000 });
}

/** Calls change-password from the signed-in page. True when the backend accepted it. */
export async function changeE2ePassword(
  page: Page,
  oldPassword: string,
  newPassword: string,
): Promise<boolean> {
  const result = await iamApi<ChangePasswordResult>(page, "POST", CHANGE_PASSWORD_PATH, {
    oldPassword,
    newPassword,
  });
  return result.status === 200 && result.json?.isSuccess === true;
}

/**
 * Sets the account back to `E2E_PASSWORD`. `candidates` are the passwords it may have right
 * now; `E2E_PASSWORD` itself is always tried last, which also clears the failed-login counter.
 */
export async function restoreE2ePassword(page: Page, candidates: string[] = []): Promise<boolean> {
  const original = e2eCredentials().password;
  await onSignedInPage(page);
  for (const current of [...new Set([...candidates, original])]) {
    if (await changeE2ePassword(page, current, original)) {
      return true;
    }
  }
  return false;
}

/** Clears the failed-login counter after a spec submitted a wrong password on purpose. */
export async function resetE2eFailedLogins(page: Page): Promise<void> {
  if (!(await restoreE2ePassword(page))) {
    throw new Error(
      "Could not clear the shared E2E account's failed-login counter. Later logins may need a CAPTCHA.",
    );
  }
}
