import { expect, type Page } from "@playwright/test";
import { e2eCredentials } from "./env";

/** IAM OIDC login fields — ids on some builds, labels on others. */
function oidcEmailField(page: Page) {
  return page.locator("#oidc-email").or(page.getByRole("textbox", { name: "Work Email" }));
}

function oidcPasswordField(page: Page) {
  return page.locator("#oidc-password").or(page.getByRole("textbox", { name: "Password" }));
}

const loginCta = (page: Page) =>
  page.getByRole("button", { name: "Log in to your account" });

const profileReady = (page: Page) =>
  page.getByRole("heading", { name: "Account details" });

const isProfileUrl = (page: Page) => /\/app\/profile/.test(page.url());

async function waitForProfile(page: Page) {
  await expect(page).toHaveURL(/\/app\/profile/, { timeout: 45_000 });
  await expect(profileReady(page)).toBeVisible({ timeout: 20_000 });
}

export async function loginThroughOidc(page: Page, options?: { loginPath?: string }) {
  const { email, password } = e2eCredentials();
  const loginPath = options?.loginPath ?? "/login";

  await page.goto(loginPath, { waitUntil: "domcontentloaded" });

  const cta = loginCta(page);
  await Promise.race([
    page.waitForURL(/\/app\/profile/, { timeout: 30_000 }),
    cta.waitFor({ state: "visible", timeout: 30_000 }),
  ]);

  if (isProfileUrl(page)) {
    await expect(profileReady(page)).toBeVisible({ timeout: 20_000 });
    return;
  }

  // Login CTA can win the race while an authenticated session is still
  // redirecting /login → /app/profile. Don't start OIDC in that case.
  if (await page.waitForURL(/\/app\/profile/, { timeout: 3_000 }).then(() => true).catch(() => false)) {
    await expect(profileReady(page)).toBeVisible({ timeout: 20_000 });
    return;
  }

  await expect(cta).toBeVisible({ timeout: 10_000 });

  const emailField = oidcEmailField(page);
  for (let attempt = 0; attempt < 3; attempt++) {
    if (isProfileUrl(page)) {
      await expect(profileReady(page)).toBeVisible({ timeout: 20_000 });
      return;
    }
    if (await cta.isVisible().catch(() => false)) {
      await cta.click().catch(() => {});
    }
    const reachedOidc = await emailField
      .waitFor({ state: "visible", timeout: 8_000 })
      .then(() => true)
      .catch(() => false);
    if (reachedOidc) break;
  }

  if (isProfileUrl(page)) {
    await expect(profileReady(page)).toBeVisible({ timeout: 20_000 });
    return;
  }

  await expect(emailField).toBeVisible({ timeout: 10_000 });
  await emailField.fill(email);

  const passwordField = oidcPasswordField(page);
  await expect(passwordField).toBeVisible({ timeout: 10_000 });
  await passwordField.fill(password);

  await page.getByRole("button", { name: "Login", exact: true }).click();
  await waitForProfile(page);
}

/**
 * Chromium specs already load `fixtures/auth.json`. Only run OIDC if that
 * session expired and /app/profile sent us to login.
 */
export async function ensureAuthenticated(page: Page) {
  await page.goto("/app/profile", { waitUntil: "domcontentloaded" });

  if (isProfileUrl(page)) {
    await expect(profileReady(page)).toBeVisible({ timeout: 30_000 });
    return;
  }

  await loginThroughOidc(page);
}

/** Force a full OIDC login (ignores any saved session). */
export async function loginFresh(page: Page) {
  await loginThroughOidc(page, { loginPath: "/" });
}
