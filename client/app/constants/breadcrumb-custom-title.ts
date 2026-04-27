export const BREADCRUMB_CUSTOM_TITLES: Record<string, string | null> = {
  "/services/authentication/users": "Users",
  "/services/authentication/organizations": "Organizations",
  "/services/authentication/client-credential": "Client Credential",
};

/** Parent segments from useRoutePathSegments often point at URLs with no route; map them to the real list pages. */
export const BREADCRUMB_LINK_OVERRIDES: Record<string, string> = {
  "/services/iam/user-detail": "/services/authentication/users",
  "/services/iam/role-detail": "/services/iam?tab=roles",
  "/services/iam/organization-detail": "/services/authentication/organizations",
  "/services/iam/permission-detail": "/services/iam?tab=permissions",
};
