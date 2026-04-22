export const CLOUD_BUILD_ENDPOINTS = {
  // Authentication & Authorization
  ACCESS_TOKEN: "/cloudbuild/v1/auth/accessToken",
  IS_AUTHORIZED: "/cloudbuild/v1/auth/isAuthorized",
  REMOVE_AUTHORIZATION: "/cloudbuild/v1/auth/removeAuthorization",
  REMOVE_ACCESS_TOKEN: "/cloudbuild/v1/auth/removeAccessToken",

  // GitHub Repositories
  GITHUB_REPOS: "/cloudbuild/v1/github/repos",
  GITHUB_USER: "/cloudbuild/v1/github/user",
  GITHUB_BRANCHES: "/cloudbuild/v1/github/branches",
  GITHUB_BRANCH_EXISTS: "/cloudbuild/v1/github/branchExists",

  // Build & Deployment
  BUILD_BUILD: "/cloudbuild/v1/build/clone",
  RUN_BUILD: "/cloudbuild/v1/build/run",
  MANUAL: "/cloudbuild/v1/build/manual",
  BUILD: "/cloudbuild/v1/build",

  // Repository Management
  REPOS: "/cloudbuild/v1/repos",
  REPOS_LIST: "/cloudbuild/v1/repos/list",
  REPO_DETAILS: "/cloudbuild/v1/repos/details",

  // Build Settings
  SETTINGS: "/cloudbuild/v1/settings",
};

export const MIGRATION_ENDPOINTS = {
  GET_STATUS: "/api/identifier/migration/status",
  START_MIGRATION: "/api/identifier/migration/start",
};
