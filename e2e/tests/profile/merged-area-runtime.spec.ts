import { test, expect } from "../../support/test-base";
import { ensureAuthenticated } from "../../support/login-helper";

/**
 * Runtime configuration and routing of the IAM client on a deployed host:
 * - #685: `BLOCKS_ALLOWED_SERVICES` reaches the page (the genesis-os app switcher filter); before it,
 *   IAM never passed it and the switcher showed every tile.
 * - #688 (blocks-os-net-react#18): logic-area calls go through `getMergedAreaBaseUrl` — the logic key
 *   while set, else `BLOCKS_OS_BASE_URL` — so nothing calls a retired standalone logic host, and the CSP
 *   allows the OS host's WebSocket origin (NotificationHub).
 */

type RuntimeEnv = Record<string, string | undefined>;

const isPlaceholder = (value: string | undefined) => !value || /^__BLOCKS_[A-Z_]+__$/.test(value);

/** The value `getMergedAreaBaseUrl("BLOCKS_LOGIC_BASE_URL")` resolves to. */
const mergedLogicBase = (env: RuntimeEnv) =>
  (isPlaceholder(env.BLOCKS_LOGIC_BASE_URL) ? env.BLOCKS_OS_BASE_URL : env.BLOCKS_LOGIC_BASE_URL)!;

test.describe("Runtime config and merged-area routing (#685, #688)", () => {
  test("allowed services, OS WebSocket CSP and no call to a standalone logic host", async ({
    page,
  }) => {
    const requestedHosts = new Set<string>();
    page.on("request", (r) => requestedHosts.add(new URL(r.url()).host));

    await ensureAuthenticated(page);
    const profile = await page.reload({ waitUntil: "domcontentloaded" });
    expect(profile, "profile document response").toBeTruthy();
    const env = await page.evaluate(
      () => (window as unknown as { __BLOCKS_ENV__: RuntimeEnv }).__BLOCKS_ENV__,
    );

    await test.step("[Positive] #685: BLOCKS_ALLOWED_SERVICES is filled from FrontendRuntime", async () => {
      expect(isPlaceholder(env.BLOCKS_ALLOWED_SERVICES), "BLOCKS_ALLOWED_SERVICES placeholder").toBe(
        false,
      );
      const services = env.BLOCKS_ALLOWED_SERVICES!.split(",").map((s) => s.trim());
      expect(services.length).toBeGreaterThan(0);
      for (const service of services) expect(service).toMatch(/^blocks-[a-z-]+$/);
    });

    await test.step("[Positive] #688: CSP connect-src allows the OS host over https and wss", async () => {
      expect(isPlaceholder(env.BLOCKS_OS_BASE_URL), "BLOCKS_OS_BASE_URL").toBe(false);
      const os = new URL(env.BLOCKS_OS_BASE_URL!);
      const csp = (await profile!.allHeaders())["content-security-policy"] ?? "";
      const connectSrc = csp
        .split(";")
        .map((d) => d.trim())
        .find((d) => d.startsWith("connect-src "));
      expect(connectSrc, "connect-src directive").toBeTruthy();
      const sources = connectSrc!.split(/\s+/);
      expect(sources).toContain(os.origin);
      expect(sources).toContain(`wss://${os.host}`);
    });

    await test.step("[Positive] #688: logic-area calls resolve to the merged-area host", async () => {
      const logic = new URL(mergedLogicBase(env));
      // The logic area is served by an OS-family host, never a standalone `*-logic.` host.
      expect(logic.host).not.toMatch(/(^|-)logic\./);
      // Let the profile page finish its own calls (profile image, storage) before checking hosts.
      await page.waitForLoadState("networkidle").catch(() => {});
      const logicHosts = [...requestedHosts].filter((h) => /(^|-)logic\./.test(h));
      expect(logicHosts, "requests to a standalone logic host").toEqual([]);
    });
  });
});
