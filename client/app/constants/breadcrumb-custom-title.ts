export const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {
  "/app/client-credential": "Client Credential",
  "/app/authentication": "Authentication",
  "/app/authentication/logs": "Logs",
  "/app/iam/logs": "Logs",
};

/** Parent segments from useRoutePathSegments often point at URLs with no route; map them to the real list pages. */
export const BREADCRUMB_LINK_OVERRIDES: Record<string, string> = {
  "/app/role-detail": "/app/iam?tab=roles",
  "/app/permission-detail": "/app/iam?tab=permissions",
};
