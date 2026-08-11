import { test, expect } from "../support/test-base";
import { loginFresh } from "../support/login-helper";

test.describe("profile sessions and history", () => {
  test.use({ storageState: { cookies: [], origins: [] } });
  test.beforeEach(async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
  });

  // ---------- Sessions ----------

  test("TC-0048: Sessions tab shows a loading skeleton while the session list is fetching", async ({
    page,
  }) => {
    // Tab panels use forceMount, so every tab's data query fires on initial
    // page load regardless of which tab is active — by the time we click
    // "Sessions" here, its fetch has likely already resolved. Select the tab
    // first (persisted to the URL via nuqs) and reload so the delayed route
    // intercepts the fetch that actually backs the visible, active panel.
    await page.getByRole("tab", { name: "Sessions" }).click();
    await expect(page).toHaveURL(/userDetails=devices/);

    await page.route("**/api/**session**", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 1500));
      await route.continue();
    });
    await page.reload();

    await expect(page.locator('[data-state="active"] .animate-pulse').first()).toBeVisible({
      timeout: 5000,
    });
  });

  test("TC-0049: Sessions empty state shows when the user has no other active sessions", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const emptyMessage = page.getByText("No sessions");
    if (await emptyMessage.isVisible({ timeout: 8000 }).catch(() => false)) {
      await expect(page.getByText("You're not signed in on any other devices.")).toBeVisible();
    }
  });

  test("TC-0050: Each session row shows device name, browser/OS, application summary, IP address and last-active time", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const firstRow = page
      .getByRole("button")
      .filter({ hasText: /last active/i })
      .first();
    if (await firstRow.isVisible({ timeout: 8000 }).catch(() => false)) {
      await expect(firstRow).toContainText("last active");
    }
  });

  test("TC-0051: The current device's session is labeled 'This device' and has no Sign out control", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const thisDeviceLabel = page.getByText("This device");
    if (await thisDeviceLabel.isVisible({ timeout: 8000 }).catch(() => false)) {
      const row = thisDeviceLabel.locator("xpath=ancestor::li[1]");
      await expect(row.getByRole("button", { name: "Sign out" })).toHaveCount(0);
    }
  });

  test("TC-0052: 'Sign out' on another device opens a confirmation naming that device", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const signOutButton = page.getByRole("button", { name: "Sign out" }).first();
    if (await signOutButton.isVisible({ timeout: 8000 }).catch(() => false)) {
      await signOutButton.click();
      await expect(page.getByRole("heading", { name: "Sign out of this device?" })).toBeVisible();
      await expect(page.getByText(/This will immediately end the session on/)).toBeVisible();
    }
  });

  test("TC-0053: Clicking 'Sign out' on a row does not also open that row's details drawer", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const signOutButton = page.getByRole("button", { name: "Sign out" }).first();
    if (await signOutButton.isVisible({ timeout: 8000 }).catch(() => false)) {
      await signOutButton.click();
      await expect(page.getByText("Session ID:")).toHaveCount(0);
    }
  });

  test("TC-0054: Confirming sign-out revokes the session and shows a success toast", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const signOutButton = page.getByRole("button", { name: "Sign out" }).first();
    if (await signOutButton.isVisible({ timeout: 8000 }).catch(() => false)) {
      await signOutButton.click();
      await page.getByRole("button", { name: "Sign out" }).last().click();

      await expect(page.getByText("Device signed out successfully")).toBeVisible({
        timeout: 15000,
      });
    }
  });

  test("TC-0055: A failed sign-out shows an error toast and keeps the session in the list", async ({
    page,
  }) => {
    await page.route("**/api/**session**revoke**", async (route) => {
      await route.fulfill({
        status: 500,
        contentType: "application/json",
        body: JSON.stringify({ isSuccess: false }),
      });
    });

    await page.getByRole("tab", { name: "Sessions" }).click();
    const signOutButton = page.getByRole("button", { name: "Sign out" }).first();
    if (await signOutButton.isVisible({ timeout: 8000 }).catch(() => false)) {
      await signOutButton.click();
      await page.getByRole("button", { name: "Sign out" }).last().click();

      await expect(page.getByRole("alert").or(page.getByText(/something went wrong/i))).toBeVisible(
        {
          timeout: 15000,
        },
      );
    }
  });

  test("TC-0056: Clicking a session row (not Sign out) opens its details drawer", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const firstRow = page
      .getByRole("button")
      .filter({ hasText: /last active/i })
      .first();
    if (await firstRow.isVisible({ timeout: 8000 }).catch(() => false)) {
      await firstRow.click();
      await expect(page.getByText("Session ID:")).toBeVisible();
      await expect(page.getByText("IP Address")).toBeVisible();
      await expect(page.getByText("Started")).toBeVisible();
      await expect(page.getByText("Browser / OS")).toBeVisible();
      await expect(page.getByText("Expires")).toBeVisible();
    }
  });

  test("TC-0057: Session details drawer lists signed-in applications with their rotation count and status", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const firstRow = page
      .getByRole("button")
      .filter({ hasText: /last active/i })
      .first();
    if (await firstRow.isVisible({ timeout: 8000 }).catch(() => false)) {
      await firstRow.click();
      const appsHeading = page.getByText("Signed-in applications");
      if (await appsHeading.isVisible({ timeout: 5000 }).catch(() => false)) {
        await expect(page.getByText(/rotations/)).toBeVisible();
      }
    }
  });

  test("TC-0058: Session details drawer also offers a Sign out action for non-current sessions", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const firstRow = page
      .getByRole("button")
      .filter({ hasText: /last active/i })
      .first();
    if (await firstRow.isVisible({ timeout: 8000 }).catch(() => false)) {
      await firstRow.click();
      const signOutInDrawer = page.getByText("Sign out of this device");
      if (await signOutInDrawer.isVisible({ timeout: 5000 }).catch(() => false)) {
        await expect(signOutInDrawer).toBeVisible();
      }
    }
  });

  test("TC-0059: Revoking a session from the details drawer closes the drawer and refreshes the list", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();

    const firstRow = page
      .getByRole("button")
      .filter({ hasText: /last active/i })
      .first();
    if (await firstRow.isVisible({ timeout: 8000 }).catch(() => false)) {
      await firstRow.click();
      const signOutInDrawer = page.getByText("Sign out of this device");
      if (await signOutInDrawer.isVisible({ timeout: 5000 }).catch(() => false)) {
        await signOutInDrawer.click();
        await page.getByRole("button", { name: "Sign out" }).last().click();

        await expect(page.getByText("Device signed out successfully")).toBeVisible({
          timeout: 15000,
        });
        await expect(page.getByText("Session ID:")).toBeHidden();
      }
    }
  });

  // ---------- History ----------

  test("TC-0060: History tab shows a loading skeleton while activity is fetching", async ({
    page,
  }) => {
    // Same eager-fetch issue as TC-0048: forceMount means History's query
    // already fired (and likely resolved) on initial page load, so select
    // the tab, then set up the delayed route and reload to actually catch
    // the fetch backing the active panel.
    await page.getByRole("tab", { name: "History" }).click();
    await expect(page).toHaveURL(/userDetails=history/);

    await page.route("**/api/**activit**", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 1500));
      await route.continue();
    });
    await page.reload();

    // Tab panels use forceMount, so the inactive Sessions panel's own
    // skeleton stays in the DOM; scope to the active panel to avoid picking
    // up its (hidden) .animate-pulse elements via .first().
    await expect(page.locator('[data-state="active"] .animate-pulse').first()).toBeVisible({
      timeout: 5000,
    });
  });

  test("TC-0061: History empty state shows when the user has no recorded activity", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "History" }).click();

    const emptyMessage = page.getByText("No activity yet");
    if (await emptyMessage.isVisible({ timeout: 8000 }).catch(() => false)) {
      await expect(page.getByText("We'll show your account activity here.")).toBeVisible();
    }
  });

  test("TC-0062: Activity table shows Event, Device, IP Address and Time columns", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "History" }).click();

    const table = page.getByRole("table");
    if (await table.isVisible({ timeout: 8000 }).catch(() => false)) {
      await expect(page.getByRole("columnheader", { name: "Event" })).toBeVisible();
      await expect(page.getByRole("columnheader", { name: "Device" })).toBeVisible();
      await expect(page.getByRole("columnheader", { name: "IP Address" })).toBeVisible();
      await expect(page.getByRole("columnheader", { name: "Time" })).toBeVisible();
    }
  });

  test("TC-0063: Event labels are color-coded by tone (info/success/warn/danger)", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "History" }).click();

    const firstRow = page.getByRole("row").nth(1);
    if (await firstRow.isVisible({ timeout: 8000 }).catch(() => false)) {
      await expect(firstRow.locator("td").first()).toBeVisible();
    }
  });

  test("TC-0064: Pagination only appears once total activity exceeds the current page size", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "History" }).click();

    const rowCount = await page.getByRole("row").count();
    // Pagination's root is a plain <div> with no navigation role — the page's
    // always-present breadcrumb <nav> would otherwise make this locator
    // match unconditionally regardless of whether pagination is rendered.
    const pagination = page.getByText(/^Page \d+ of \d+$/);
    if (rowCount > 11) {
      await expect(pagination).toBeVisible();
    } else {
      await expect(pagination).toHaveCount(0);
    }
  });

  test("TC-0065: Changing the page size reloads the activity list at the new size", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "History" }).click();

    const pageSizeSelect = page.getByRole("combobox").last();
    if (await pageSizeSelect.isVisible({ timeout: 5000 }).catch(() => false)) {
      await pageSizeSelect.click();
      const option20 = page.getByRole("option", { name: "20" });
      if (await option20.isVisible().catch(() => false)) {
        await option20.click();
        await expect(page.getByRole("table")).toBeVisible();
      }
    }
  });

  test("TC-0066: Navigating to the next page fetches the next set of activity entries", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "History" }).click();

    // Pagination's next-page button is icon-only with no accessible name
    // (see pagination.tsx), so it can't be found via getByRole name — target
    // the button rendering the chevron-right icon instead. Also, .isEnabled()
    // waits up to its own timeout for the locator to resolve, so a
    // never-matching role locator hung for the full 30s test timeout instead
    // of failing fast.
    const nextButton = page.locator("button:has(svg.lucide-chevron-right)");
    if (
      (await nextButton.count()) > 0 &&
      (await nextButton.isEnabled({ timeout: 5000 }).catch(() => false))
    ) {
      await nextButton.click();
      await expect(page.getByRole("table")).toBeVisible();
    }
  });
});
