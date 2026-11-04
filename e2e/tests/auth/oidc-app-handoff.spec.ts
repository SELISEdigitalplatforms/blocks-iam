import { type Page, type Request, type Route, type TestInfo } from "@playwright/test";
import { test, expect } from "../../support/test-base";

/**
 * Native-app (app-scheme) hand-off on the login SPA — blocks-iam#617, spec section 10
 * "Automated SPA checks" steps 1–8, section 4 (design contract) and section 5 (preservation).
 *
 * Every backend call that decides the outcome is stubbed, the same way oidc-login.spec.ts does
 * it: ONE persistent handler per endpoint, registered in beforeEach, whose answer is swapped
 * through module-level state. No real credentials are typed; the password login is stubbed.
 *
 * Proving "one attempted navigation" to an app scheme: Chromium raises no network request for
 * an unknown scheme and leaves the page where it is, so page.on("request") never sees it. The
 * renderer still reports the attempt over CDP as Page.frameRequestedNavigation (reason
 * "scriptInitiated"), once per `window.location.href = …` assignment. Those events are counted
 * per URL. The page URL is also asserted to stay put, which is what a user without the app sees.
 */

const APP_SCHEME_REDIRECT_URI = "com.blocks.e2e.app://login/callback";
const APP_LOGIN_REDIRECT = `${APP_SCHEME_REDIRECT_URI}?code=abc&state=xyz`;
const APP_CALLBACK_REDIRECT = `${APP_SCHEME_REDIRECT_URI}?code=1&state=s`;
const WEB_LOGIN_REDIRECT = "https://example.com/cb?code=abc&state=xyz";
const PROVIDER_AUTHORIZE_URL =
  "https://login.microsoftonline.com/common/oauth2/v2.0/authorize?client_id=e2e-ms-client&state=e2e";
// Never followed: a C12 error body carries it to prove the SPA ignores URLs in a failed response.
const FOREIGN_URL = "https://evil.example/steal";

const TENANT = "e2e-tenant";
const HANDOFF_MARKER_KEY = "oidc_app_handoff";
const NO_REDIRECT_TARGET = "Login succeeded but no redirect target was provided";

const loginUrl = (redirectUri: string, extra = "") =>
  "/oidc/login?client_id=test-client&redirect_uri=" +
  encodeURIComponent(redirectUri) +
  "&scope=openid&state=xyz&response_type=code" +
  extra;
const APP_LOGIN_URL = loginUrl(APP_SCHEME_REDIRECT_URI);
const WEB_LOGIN_URL = loginUrl("https://example.com/cb");
const MFA_URL = "/oidc/mfa-check?mfa_id=mfa-id-617&mfa_type=2";
const CALLBACK_URL = `/oidc/microsoft/callback/${TENANT}?code=c&state=s`;
const CALLBACK_API = /\/api\/oidc\/callback(\?|$)/;

type StubResponse = {
  status?: number;
  contentType?: string;
  body: Record<string, unknown> | string;
};

// Module-level dispatcher state (config: workers: 1). Reset in beforeEach.
let nextLoginResponse: StubResponse | null = null;
let nextMfaResponse: StubResponse | null = null;
let nextCallbackResponse: StubResponse | "abort" | null = null;
let loginOptionsBody: Record<string, unknown> | null = null;
let loginPosts: Request[] = [];
let callbackRequests: { resourceType: string; accept: string; url: string }[] = [];
let foreignRequests: string[] = [];
let providerRedirects = 0;

const fulfillWith = (route: Route, stub: StubResponse) =>
  route.fulfill({
    status: stub.status ?? 200,
    contentType: stub.contentType ?? "application/json",
    body: typeof stub.body === "string" ? stub.body : JSON.stringify(stub.body),
  });

/**
 * Counts script-initiated top-level navigation attempts per URL, including app schemes that
 * never become a network request (see the file header).
 */
const watchNavigationAttempts = async (page: Page) => {
  const attempts: string[] = [];
  const cdp = await page.context().newCDPSession(page);
  await cdp.send("Page.enable");
  cdp.on("Page.frameRequestedNavigation", (event) => attempts.push(event.url));
  return {
    to: (url: string) => attempts.filter((u) => u === url).length,
    all: () => [...attempts],
  };
};

/** Main-frame document requests (real top-level navigations) whose URL starts with `prefix`. */
const watchDocumentNavigations = (page: Page, prefix: string) => {
  const seen: string[] = [];
  page.on("request", (request) => {
    if (
      request.isNavigationRequest() &&
      request.frame() === page.mainFrame() &&
      request.url().startsWith(prefix)
    ) {
      seen.push(request.url());
    }
  });
  return seen;
};

const submitPasswordLogin = async (page: Page) => {
  // Stubbed endpoint: placeholder values, never the real E2E account.
  await page.locator("#oidc-email").fill("e2e@example.test");
  await page.locator("#oidc-password").fill("anypass1!");
  await page.getByRole("button", { name: "Login", exact: true }).click();
};

const submitMfaCode = async (page: Page) => {
  await page.locator('input[maxlength="6"]').fill("123456");
  await page.getByRole("button", { name: /^verify$/i }).click();
};

const openLogin = async (page: Page, url = APP_LOGIN_URL) => {
  await page.goto(url, { waitUntil: "domcontentloaded" });
  await expect(page.locator("#oidc-email")).toBeVisible({ timeout: 30_000 });
};

const handoffBlock = (page: Page) => page.getByRole("status").filter({ hasText: "Signed in" });
const returnLink = (page: Page) => page.getByRole("link", { name: "Return to the app" });

/** Section 4 block: heading, body copy and a link whose href is exactly the redirect URL. */
const expectHandoffBlock = async (page: Page, url: string) => {
  const block = handoffBlock(page);
  await expect(block).toBeVisible({ timeout: 15_000 });
  await expect(block).toHaveAttribute("aria-live", "polite");
  await expect(block.getByRole("heading", { name: "Signed in" })).toBeVisible();
  await expect(
    block.getByText("Returning you to the app. If it does not open, tap the button below."),
  ).toBeVisible();
  await expect(returnLink(page)).toHaveAttribute("href", url);
};

/** Sets the hand-off marker in this origin's sessionStorage, as sso-signin-card would. */
const seedMarker = async (page: Page) => {
  await page.goto("/oidc/error", { waitUntil: "domcontentloaded" });
  await page.evaluate((key) => sessionStorage.setItem(key, "1"), HANDOFF_MARKER_KEY);
};

const readMarker = (page: Page) =>
  page.evaluate((key) => sessionStorage.getItem(key), HANDOFF_MARKER_KEY);

/** Waits for /oidc/error and returns its query string exactly as the SPA built it. */
const errorPageSearch = async (page: Page) => {
  await page.waitForURL((u) => u.pathname === "/oidc/error", { timeout: 15_000 });
  return new URL(page.url()).search;
};

// The link spans the card's form column at every viewport (section 4 "Layout and states").
// Compared as layout widths: the button's hover/active styles scale it with a transform, which a
// bounding box would include while that transition runs. The hand-off block fills the column, so
// the column (the block's own container) is the reference.
const expectLinkSpansCard = async (page: Page) => {
  const widths = await returnLink(page).evaluate((link) => {
    const block = link.parentElement as HTMLElement;
    const column = block.parentElement as HTMLElement;
    const columnStyle = getComputedStyle(column);
    const columnContent =
      column.clientWidth - parseFloat(columnStyle.paddingLeft) - parseFloat(columnStyle.paddingRight);
    return { link: (link as HTMLElement).offsetWidth, block: block.clientWidth, column: columnContent };
  });
  expect(Math.abs(widths.link - widths.block), JSON.stringify(widths)).toBeLessThanOrEqual(1);
  expect(Math.abs(widths.block - widths.column), JSON.stringify(widths)).toBeLessThanOrEqual(1);
};

// The hand-off link replaces the submit button under the pointer; its :hover style scales it.
const moveMouseAway = (page: Page) => page.mouse.move(0, 0);

const screenshot = async (page: Page, testInfo: TestInfo, name: string) => {
  await moveMouseAway(page);
  // outputPath() lands in the run's --output directory, never in the repository.
  const path = testInfo.outputPath(`${name}.png`);
  await page.screenshot({ path, fullPage: true, animations: "disabled" });
  await testInfo.attach(name, { path, contentType: "image/png" });
};

test.describe("OIDC native-app hand-off (blocks-iam#617)", () => {
  test.beforeEach(async ({ context, page }) => {
    // Same rationale as oidc-login.spec.ts: the setup session would skip the login form. With
    // the app-settings cookie gone the theme is "system", so emulateMedia() picks dark/light.
    await context.clearCookies();

    nextLoginResponse = null;
    nextMfaResponse = null;
    nextCallbackResponse = null;
    loginOptionsBody = null;
    loginPosts = [];
    callbackRequests = [];
    foreignRequests = [];
    providerRedirects = 0;

    // Default template (normalised from {}), as oidc-pages.spec.ts does, so the screenshots
    // show the reference design rather than whatever the target tenant has customised.
    await page.route("**/api/idp/oidc-ui-config*", (route) =>
      fulfillWith(route, { body: { captcha: null, template: {} } }),
    );

    await page.route("**/api/auth/login-options*", async (route) => {
      if (!loginOptionsBody) return route.fallback();
      await fulfillWith(route, { body: loginOptionsBody });
    });

    // Password login, MFA verify (body carries mfa_code) and the SSO initiate call (body
    // carries provider) all post to /api/oidc/login.
    await page.route("**/api/oidc/login", async (route) => {
      const body = JSON.parse(route.request().postData() || "{}");
      const next = body.mfa_code ? nextMfaResponse : nextLoginResponse;
      if (!next) return route.fallback();
      await fulfillWith(route, next);
    });

    await page.route(CALLBACK_API, async (route) => {
      const request = route.request();
      callbackRequests.push({
        resourceType: request.resourceType(),
        accept: request.headers()["accept"] ?? "",
        url: request.url(),
      });
      const next = nextCallbackResponse;
      if (next === "abort") return route.abort("failed");
      if (!next) return route.fallback();
      await fulfillWith(route, next);
    });

    // Web redirect targets: serve a blank page so a navigation there completes offline.
    await page.route("https://example.com/**", (route) =>
      fulfillWith(route, { contentType: "text/html", body: "<!doctype html><title>app</title>" }),
    );
    await page.route("https://evil.example/**", async (route) => {
      foreignRequests.push(route.request().url());
      await route.abort();
    });

    // Identity provider: answer 204 so Chromium cancels the navigation and the login page
    // stays the current document; the test then reads the marker it left in sessionStorage.
    // (Reading it from here, while the navigation is in flight, races the page.)
    await page.route("https://login.microsoftonline.com/**", async (route) => {
      providerRedirects += 1;
      await route.fulfill({ status: 204, body: "" });
    });

    page.on("request", (request) => {
      if (request.method() === "POST" && new URL(request.url()).pathname === "/api/oidc/login") {
        loginPosts.push(request);
      }
    });
  });

  test("Step 1 — H6, H7, C18: password login with an app-scheme redirect attempts it once and replaces the form with the hand-off", async ({
    page,
  }) => {
    const nav = await watchNavigationAttempts(page);
    nextLoginResponse = { body: { redirect_uri: APP_LOGIN_REDIRECT } };
    await openLogin(page);
    const loginPageUrl = page.url();

    await submitPasswordLogin(page);

    await test.step("H7: hand-off block with the exact href replaces the form and takes focus", async () => {
      await expectHandoffBlock(page, APP_LOGIN_REDIRECT);
      await expect(returnLink(page)).toBeFocused();
      await expect(page.locator("#oidc-email")).toHaveCount(0);
      await expect(page.locator("#oidc-password")).toHaveCount(0);
      // Approved decision: no "Access Granted" success animation on the app-scheme path.
      await expect(page.getByText("Access Granted")).toHaveCount(0);
    });

    await test.step("H6, C18: exactly one attempted navigation to the exact redirect string; the page stays", async () => {
      await expect.poll(() => nav.to(APP_LOGIN_REDIRECT), { timeout: 10_000 }).toBe(1);
      // Give a stray second effect run time to show up before asserting "once". The preview is
      // a production build (no StrictMode double effects), so C18 is observable here only as
      // "one assignment per mount across re-renders"; the strict-mode case is the unit test's.
      await page.waitForTimeout(1_500);
      expect(nav.to(APP_LOGIN_REDIRECT)).toBe(1);
      expect(page.url()).toBe(loginPageUrl);
      expect(loginPosts).toHaveLength(1);
    });
  });

  test("Step 2 — H8: MFA verification with an app-scheme redirect attempts it once and shows the hand-off", async ({
    page,
  }) => {
    const nav = await watchNavigationAttempts(page);
    nextMfaResponse = { body: { redirect_uri: APP_LOGIN_REDIRECT } };
    await page.goto(MFA_URL, { waitUntil: "domcontentloaded" });
    await expect(page.locator('input[maxlength="6"]')).toBeVisible({ timeout: 30_000 });
    const mfaPageUrl = page.url();

    await submitMfaCode(page);

    await expectHandoffBlock(page, APP_LOGIN_REDIRECT);
    await expect(returnLink(page)).toBeFocused();
    await expect(page.locator('input[maxlength="6"]')).toHaveCount(0);
    await expect.poll(() => nav.to(APP_LOGIN_REDIRECT), { timeout: 10_000 }).toBe(1);
    await page.waitForTimeout(1_500);
    expect(nav.to(APP_LOGIN_REDIRECT)).toBe(1);
    expect(page.url()).toBe(mfaPageUrl);
  });

  test("Step 3 — C10: a redirect_uri failing both rules is not followed on password login or MFA", async ({
    page,
  }) => {
    const nav = await watchNavigationAttempts(page);
    const dialogs: string[] = [];
    page.on("dialog", (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });

    await test.step("C10 password login: error shown, no navigation, still on /oidc/login", async () => {
      nextLoginResponse = { body: { redirect_uri: "javascript:alert(1)" } };
      await openLogin(page);
      const before = page.url();
      await submitPasswordLogin(page);

      await expect(page.getByText(NO_REDIRECT_TARGET).first()).toBeVisible({ timeout: 20_000 });
      await expect(handoffBlock(page)).toHaveCount(0);
      expect(page.url()).toBe(before);
    });

    await test.step("C10 MFA: error in the form's error area, form still usable, no navigation", async () => {
      nextMfaResponse = { body: { redirect_uri: "javascript:alert(1)" } };
      await page.goto(MFA_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator('input[maxlength="6"]')).toBeVisible({ timeout: 30_000 });
      const before = page.url();
      await submitMfaCode(page);

      await expect(page.getByText(NO_REDIRECT_TARGET).first()).toBeVisible({ timeout: 20_000 });
      await expect(page.locator('input[maxlength="6"]')).toBeVisible();
      await expect(handoffBlock(page)).toHaveCount(0);
      expect(page.url()).toBe(before);
    });

    // page.goto() is not script-initiated, so any requested navigation would be the SPA's.
    expect(nav.all()).toEqual([]);
    expect(dialogs).toEqual([]);
  });

  test("Step 4 — C11: the error dialog action with a refused redirect_uri closes the dialog and stays", async ({
    page,
  }) => {
    const nav = await watchNavigationAttempts(page);
    const dialogs: string[] = [];
    page.on("dialog", (dialog) => {
      dialogs.push(dialog.message());
      void dialog.dismiss();
    });

    await page.goto(
      loginUrl(
        "javascript:alert(1)",
        "&error=invalid_request&error_description=Invalid%20redirect_uri",
      ),
      { waitUntil: "domcontentloaded" },
    );
    const before = page.url();

    const dialog = page.getByRole("alertdialog", { name: /sign-in unavailable/i });
    await expect(dialog).toBeVisible({ timeout: 30_000 });
    await dialog.getByRole("button").click();

    await expect(dialog).toHaveCount(0);
    await expect(page.locator("#oidc-email")).toBeEnabled();
    await page.waitForTimeout(1_000);
    expect(page.url()).toBe(before);
    expect(nav.all()).toEqual([]);
    expect(dialogs).toEqual([]);
  });

  test("Step 5 — H9: Sign in with Microsoft sets the hand-off marker only for an app-scheme redirect_uri", async ({
    page,
  }) => {
    loginOptionsBody = {
      allowedGrantTypes: ["password", "social"],
      ssoInfo: [
        { provider: "microsoft", clientId: "e2e-ms-client", redirectUris: ["https://example.com/sso"] },
      ],
    };
    nextLoginResponse = { body: { authorizationUrl: PROVIDER_AUTHORIZE_URL } };
    const microsoftButton = page.getByRole("button", { name: /sign in with microsoft/i });

    await test.step("H9 app-scheme redirect_uri: marker is \"1\" before leaving for the provider", async () => {
      await openLogin(page, APP_LOGIN_URL);
      await microsoftButton.click();
      await expect.poll(() => providerRedirects, { timeout: 15_000 }).toBe(1);
      expect(await readMarker(page)).toBe("1");
      const initiate = loginPosts.map((r) => JSON.parse(r.postData() || "{}")).find((b) => b.provider);
      expect(initiate?.provider).toBe("microsoft");
      expect(initiate?.redirect_uri).toBe(APP_SCHEME_REDIRECT_URI);
    });

    await test.step("H9 https redirect_uri: a stale marker is removed, so it is absent", async () => {
      providerRedirects = 0;
      await openLogin(page, WEB_LOGIN_URL);
      await page.evaluate((key) => sessionStorage.setItem(key, "1"), HANDOFF_MARKER_KEY);
      await microsoftButton.click();
      await expect.poll(() => providerRedirects, { timeout: 15_000 }).toBe(1);
      expect(await readMarker(page)).toBeNull();
    });
  });

  test("Step 6 — H10, C12: Microsoft callback with the marker fetches JSON and hands off, or routes errors to /oidc/error", async ({
    page,
  }) => {
    const nav = await watchNavigationAttempts(page);

    await test.step("H10: marker removed, one JSON fetch, one attempt, hand-off inside the themed shell", async () => {
      nextCallbackResponse = { body: { redirect_uri: APP_CALLBACK_REDIRECT } };
      await seedMarker(page);
      await page.goto(CALLBACK_URL, { waitUntil: "domcontentloaded" });

      await expectHandoffBlock(page, APP_CALLBACK_REDIRECT);
      await expect(returnLink(page)).toBeFocused();
      await expect(page.locator(".oidc-scifi-root").filter({ has: handoffBlock(page) })).toBeVisible();
      expect(await readMarker(page)).toBeNull();

      expect(callbackRequests).toHaveLength(1);
      expect(callbackRequests[0].resourceType).toBe("fetch");
      expect(callbackRequests[0].accept).toContain("application/json");
      const query = new URL(callbackRequests[0].url).searchParams;
      expect(query.get("code")).toBe("c");
      expect(query.get("state")).toBe("s");
      expect(query.get("tenant_id")).toBe(TENANT);

      await expect.poll(() => nav.to(APP_CALLBACK_REDIRECT), { timeout: 10_000 }).toBe(1);
      await page.waitForTimeout(1_500);
      expect(nav.to(APP_CALLBACK_REDIRECT)).toBe(1);
      expect(new URL(page.url()).pathname).toBe(`/oidc/microsoft/callback/${TENANT}`);
    });

    await test.step("6.5: marker + 200 with a web redirect_uri navigates the page there", async () => {
      callbackRequests = [];
      const webNavs = watchDocumentNavigations(page, "https://example.com/");
      nextCallbackResponse = { body: { redirect_uri: WEB_LOGIN_REDIRECT } };
      await seedMarker(page);
      await page.goto(CALLBACK_URL, { waitUntil: "domcontentloaded" });
      await page.waitForURL(WEB_LOGIN_REDIRECT, { timeout: 15_000 });
      expect(webNavs).toEqual([WEB_LOGIN_REDIRECT]);
      expect(callbackRequests.map((r) => r.resourceType)).toEqual(["fetch"]);
    });

    await test.step("C12: 400 → /oidc/error with the response's error strings, never its URLs", async () => {
      nextCallbackResponse = {
        status: 400,
        body: {
          error: "access_denied",
          error_description: "Sign-up is disabled",
          redirect_uri: FOREIGN_URL,
        },
      };
      await seedMarker(page);
      await page.goto(CALLBACK_URL, { waitUntil: "domcontentloaded" });
      expect(await errorPageSearch(page)).toBe(
        `?error=access_denied&error_description=Sign-up%20is%20disabled&tenant_id=${TENANT}`,
      );
    });

    await test.step("C12: 200 without redirect_uri → server_error with the default description", async () => {
      nextCallbackResponse = { body: { success: true } };
      await seedMarker(page);
      await page.goto(CALLBACK_URL, { waitUntil: "domcontentloaded" });
      expect(await errorPageSearch(page)).toBe(
        `?error=server_error&error_description=Sign-in%20could%20not%20be%20completed.&tenant_id=${TENANT}`,
      );
    });

    await test.step("C12: network failure → server_error with the default description", async () => {
      nextCallbackResponse = "abort";
      await seedMarker(page);
      await page.goto(CALLBACK_URL, { waitUntil: "domcontentloaded" });
      expect(await errorPageSearch(page)).toBe(
        `?error=server_error&error_description=Sign-in%20could%20not%20be%20completed.&tenant_id=${TENANT}`,
      );
    });

    expect(foreignRequests).toEqual([]);
    expect(nav.all().filter((u) => u.startsWith(FOREIGN_URL))).toEqual([]);
  });

  test("Step 7 — C13 and section 5: without the marker (or without a code) the callback navigates as today and never fetches", async ({
    page,
  }) => {
    // Stand-in for the backend's 302: any document answer ends the navigation offline.
    nextCallbackResponse = { contentType: "text/html", body: "<!doctype html><title>callback</title>" };

    await test.step("C13 / section 5 web Microsoft callback: one top-level navigation, zero fetches", async () => {
      await page.goto("/oidc/error", { waitUntil: "domcontentloaded" });
      await page.evaluate((key) => sessionStorage.removeItem(key), HANDOFF_MARKER_KEY);
      await page.goto(CALLBACK_URL, { waitUntil: "domcontentloaded" });
      await page.waitForURL(CALLBACK_API, { timeout: 15_000 });

      expect(callbackRequests.map((r) => r.resourceType)).toEqual(["document"]);
      const query = new URL(page.url()).searchParams;
      expect(query.get("code")).toBe("c");
      expect(query.get("state")).toBe("s");
      expect(query.get("tenant_id")).toBe(TENANT);
    });

    await test.step("Approved decision: marker without a code is removed and the page navigates as today", async () => {
      callbackRequests = [];
      await seedMarker(page);
      await page.goto(
        `/oidc/microsoft/callback/${TENANT}?error=access_denied&error_description=cancelled`,
        { waitUntil: "domcontentloaded" },
      );
      await page.waitForURL(CALLBACK_API, { timeout: 15_000 });

      expect(callbackRequests.map((r) => r.resourceType)).toEqual(["document"]);
      expect(new URL(page.url()).searchParams.get("error")).toBe("access_denied");
      // Same origin as the callback page, so this reads the sessionStorage it left behind.
      expect(await readMarker(page)).toBeNull();
    });
  });

  test("Section 5 preservation — http(s) password login makes one POST /api/oidc/login and one top-level navigation", async ({
    page,
  }) => {
    const nav = await watchNavigationAttempts(page);
    const webNavs = watchDocumentNavigations(page, "https://example.com/");
    nextLoginResponse = { body: { redirect_uri: WEB_LOGIN_REDIRECT } };
    await openLogin(page, WEB_LOGIN_URL);

    await submitPasswordLogin(page);
    // The web path keeps the success animation before navigating, so allow for it.
    await page.waitForURL(WEB_LOGIN_REDIRECT, { timeout: 30_000 });

    expect(loginPosts).toHaveLength(1);
    expect(webNavs).toEqual([WEB_LOGIN_REDIRECT]);
    expect(nav.to(WEB_LOGIN_REDIRECT)).toBe(1);
    expect(nav.all().filter((u) => u.startsWith(APP_SCHEME_REDIRECT_URI))).toEqual([]);
  });

  test("Step 8 — section 4 visual: hand-off screenshots (390×844 dark/light, 1440×900) and keyboard focus", async ({
    page,
  }, testInfo) => {
    const nav = await watchNavigationAttempts(page);

    for (const scheme of ["dark", "light"] as const) {
      await test.step(`Password hand-off at 390×844, ${scheme}`, async () => {
        await page.setViewportSize({ width: 390, height: 844 });
        await page.emulateMedia({ colorScheme: scheme });
        nextLoginResponse = { body: { redirect_uri: APP_LOGIN_REDIRECT } };
        await openLogin(page);
        await submitPasswordLogin(page);
        await expectHandoffBlock(page, APP_LOGIN_REDIRECT);
        await expect(page.locator(".oidc-scifi-root").first()).toHaveAttribute("data-theme", scheme);
        await expectLinkSpansCard(page);
        await screenshot(page, testInfo, `password-handoff-390x844-${scheme}`);
      });

      await test.step(`Microsoft callback hand-off at 390×844, ${scheme}`, async () => {
        await page.setViewportSize({ width: 390, height: 844 });
        await page.emulateMedia({ colorScheme: scheme });
        nextCallbackResponse = { body: { redirect_uri: APP_CALLBACK_REDIRECT } };
        await seedMarker(page);
        await page.goto(CALLBACK_URL, { waitUntil: "domcontentloaded" });
        await expectHandoffBlock(page, APP_CALLBACK_REDIRECT);
        await expect(page.locator(".oidc-scifi-root").first()).toHaveAttribute("data-theme", scheme);
        await screenshot(page, testInfo, `microsoft-callback-handoff-390x844-${scheme}`);
      });
    }

    await test.step("Password hand-off at 768×1024, dark: the link spans the card", async () => {
      await page.setViewportSize({ width: 768, height: 1024 });
      await page.emulateMedia({ colorScheme: "dark" });
      nextLoginResponse = { body: { redirect_uri: APP_LOGIN_REDIRECT } };
      await openLogin(page);
      await submitPasswordLogin(page);
      await expectHandoffBlock(page, APP_LOGIN_REDIRECT);
      await expectLinkSpansCard(page);
      await screenshot(page, testInfo, "password-handoff-768x1024-dark");
    });

    await test.step("Password hand-off at 1440×900, dark, with keyboard checks", async () => {
      await page.setViewportSize({ width: 1440, height: 900 });
      await page.emulateMedia({ colorScheme: "dark" });
      nextLoginResponse = { body: { redirect_uri: APP_LOGIN_REDIRECT } };
      await openLogin(page);
      await submitPasswordLogin(page);
      await expectHandoffBlock(page, APP_LOGIN_REDIRECT);
      await expectLinkSpansCard(page);
      await screenshot(page, testInfo, "password-handoff-1440x900-dark");

      // Tab reaches the link: start from the document, not from the mount-time focus.
      await page.evaluate(() => (document.activeElement as HTMLElement | null)?.blur());
      const link = returnLink(page);
      for (let i = 0; i < 20 && !(await link.evaluate((el) => el === document.activeElement)); i++) {
        await page.keyboard.press("Tab");
      }
      await expect(link).toBeFocused();

      // Focus ring visible: keyboard focus matches :focus-visible and draws an outline or ring.
      const ring = await link.evaluate((el) => {
        const style = getComputedStyle(el);
        return {
          focusVisible: el.matches(":focus-visible"),
          outline: style.outlineStyle !== "none" && parseFloat(style.outlineWidth) > 0,
          shadow: style.boxShadow !== "none",
        };
      });
      expect(ring.focusVisible).toBe(true);
      expect(ring.outline || ring.shadow).toBe(true);
      await screenshot(page, testInfo, "password-handoff-1440x900-dark-focus");

      // Enter activates it: one more attempt to the same app URL, page still in place.
      const before = nav.to(APP_LOGIN_REDIRECT);
      await page.keyboard.press("Enter");
      await expect.poll(() => nav.to(APP_LOGIN_REDIRECT), { timeout: 10_000 }).toBe(before + 1);
      await expect(link).toBeVisible();
    });
  });
});
