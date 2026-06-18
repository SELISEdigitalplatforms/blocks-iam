export const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {
  "/app/users": "Users",
  "/app/organizations": "Organizations",
  "/app/client-credential": "Client Credential",
  "/app/authentication": "Authentication",
  "/app/authentication/logs": "Logs",
  "/app/iam/logs": "Logs",
};

/** Parent segments from useRoutePathSegments often point at URLs with no route; map them to the real list pages. */
export const BREADCRUMB_LINK_OVERRIDES: Record<string, string> = {
  "/app/iam/user-detail": "/app/users",
  "/app/iam/role-detail": "/app/iam?tab=roles",
  "/app/iam/organization-detail": "/app/organizations",
  "/app/iam/permission-detail": "/app/iam?tab=permissions",
};