# Contributing to Blocks IAM

This document captures the conventions that a linter or analyzer **cannot** check on its own.
The mechanically enforceable rules live in `.editorconfig` (C# naming + formatting, surfaced at
build time via `server/Directory.Build.props`) and `client/.eslintrc.cjs` (TypeScript naming).
Those start at **warning** severity so the build stays green today; the plan is to fix drift and
ratchet toward **error** over time. Do not turn on `TreatWarningsAsErrors` until the warning
backlog is cleared.

## Product naming

- The product is **Blocks IAM**. Use that name on every user-facing surface (sign-in, consent,
  account selection, activation, account screens).
- **Blocks Cloud** is not this product and must not appear on IAM-owned screens.
- **IdP** refers to *external* or tenant-created identity providers (Entra, Google, Okta, Auth0,
  customer OIDC), never to this product.

## HTTP routing

- Routes are lower-case, hyphenated, and resource-oriented. Collections are plural nouns
  (`/iam/users`, `/oidc-clients`, `/refresh-tokens`).
- Prefer verbs implied by the HTTP method over verb segments: `GET` a collection or item,
  `POST` to create, `PUT`/`PATCH` to update, `DELETE` to remove. Where an explicit action is
  unavoidable, use `{resource}/{id}/{action}` (e.g. `refresh-tokens/{tokenId}/revoke`).
- Route parameters are camelCase (`{tenantId}`, `{clientId}`). Keep RFC-defined names verbatim
  where a spec requires them (`device_authorization`); do not invent new snake_case names.
- **Renames are backward compatible.** When a route changes, add the new route and keep the old
  one as a documented compatibility alias (mark it deprecated); never delete a live route.

## Controller actions

- Action method names are noun-specific and match the route + domain concept
  (`GetAuthenticationConfiguration`, `CreateUser`, `AssignRolePermissions`, `GetMfaConfig`).
  Avoid bare verbs (`Get`, `Update`, `Upsert`) and names that contradict their route.
- Do not declare request parameters that the action never reads. A parameter published in the
  signature appears in OpenAPI as a supported filter; if it does nothing, that is a lie to callers.

## Response envelope

- Application endpoints return a typed `Task<ActionResult<TResponse>>` using the shared response
  envelope. The success flag is **`IsSuccess`** — never `Success`. Do not introduce new anonymous
  error shapes or `Dictionary<string, object>` responses; model an explicit DTO instead.
- OAuth/OIDC endpoints are a documented exception: they keep the RFC `{ error, error_description }`
  shape. Keep those isolated from the application envelope.

## DTO / model naming

- Inbound payloads are `*Request`; outbound payloads are `*Response`. The repo standard is
  `*Request` / `*Response`, not `*Dto` or `*Payload`.
- Put request/response models in a `RequestModel` / `ResponseModel` (or `Models`) folder, not
  nested inside a controller class.
- Protocol-specific OAuth/OIDC payload names stay isolated and keep their spec names.

## Permission-scope grammar

- Scopes follow `service.controller.action`, e.g. `blocks-iam::mfa::mutate-mfa-configs`.
- The area segment should match the owning controller (`mfa`, `security`, `oidc-clients`), not a
  catch-all `iam`, unless an exception is explicitly documented.
- **Scopes are IAM-grant-breaking.** Changing a scope string can revoke access. A scope change is
  a data/rollout change (permission seeding, role-template updates, frontend checks) — never a
  silent code edit. Coordinate before touching one.

## C# specifics

- Interfaces are `I`-prefixed; types and public members are PascalCase; parameters and locals are
  camelCase; private fields are `_camelCase`.
- `Task`-returning methods carry the `Async` suffix.

## TypeScript specifics

- Filenames are kebab-case. Hooks are `use-*.ts(x)`; services are `*.service.ts`. Prefer named
  exports. (These filename rules are documented here rather than lint-enforced to avoid pulling in
  an extra ESLint plugin; enforcement can be added later with `eslint-plugin-unicorn`.)
- Types and React components are PascalCase; variables, functions, and parameters are camelCase.

## Cross-repo alignment

These conventions are intended to be shared across the five Blocks repos. When you change a
convention here, raise the matching change in the sibling repos so the standard does not fork.
