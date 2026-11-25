import { getRollbar } from "@seliseblocks/genesis-os/observability";
import { SERVICE_NAME } from "@/constants/service.constant";

type RollbarInstance = ReturnType<typeof getRollbar>;

/** Marks the items this module sends, so the transform below touches nothing else. */
export const OIDC_ROUTE_ERROR_SOURCE = "oidc-route-error";

type RollbarData = {
  custom?: { source?: unknown };
  request?: { url?: unknown; query_string?: unknown };
  body?: { telemetry?: { body?: Record<string, unknown> }[] };
};

/** `https://host/path?code=…#x` -> `https://host/path`; anything that is not a URL is kept. */
const withoutQuery = (value: unknown): unknown => {
  if (typeof value !== "string") return value;
  const end = value.search(/[?#]/);
  return end === -1 ? value : value.slice(0, end);
};

/**
 * Removes every query string from an item before it is sent. Rollbar adds the page URL (and the
 * telemetry trail adds earlier ones) on its own, and on these pages the query carries the
 * activation or recovery code and the application's redirect_uri.
 */
export const stripQueryStrings = (data: RollbarData): void => {
  if (data.request) {
    data.request.url = withoutQuery(data.request.url);
    delete data.request.query_string;
  }
  for (const event of data.body?.telemetry ?? []) {
    const body = event?.body;
    if (!body) continue;
    for (const key of ["url", "from", "to"]) {
      if (key in body) body[key] = withoutQuery(body[key]);
    }
  }
};

const installedOn = new WeakSet<RollbarInstance>();

/** Runs the scrub, for this module's items only, ahead of any transform already configured. */
const installTransform = (rollbar: RollbarInstance) => {
  if (installedOn.has(rollbar)) return;
  installedOn.add(rollbar);
  const previous = rollbar.options?.transform;
  rollbar.configure({
    transform: (data, item) => {
      const payload = data as RollbarData;
      if (payload.custom?.source === OIDC_ROUTE_ERROR_SOURCE) stripQueryStrings(payload);
      return previous?.(data, item);
    },
  });
};

/** A readable message for a thrown value that is not an Error, such as a router error response. */
const describeThrown = (value: unknown): string => {
  if (value && typeof value === "object") {
    const { status, statusText, message } = value as { status?: unknown; statusText?: unknown; message?: unknown };
    const parts = [status, statusText, message].filter((part) => part !== undefined && part !== "");
    if (parts.length > 0) return parts.map(String).join(" ");
  }
  return String(value);
};

/**
 * Reports a render error caught by an OIDC route's error element. Sends the route path only:
 * no query string values (activation code, redirect_uri) leave the browser with it.
 */
export const reportOidcRouteError = (error: unknown, routePath: string): void => {
  const rollbar = getRollbar({ service: SERVICE_NAME });
  installTransform(rollbar);
  const reportable = error instanceof Error ? error : new Error(describeThrown(error));
  rollbar.error(reportable, { source: OIDC_ROUTE_ERROR_SOURCE, route: withoutQuery(routePath) });
};
