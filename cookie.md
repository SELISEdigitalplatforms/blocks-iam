# Cookie Setting Issue: IdP Initiate Flow

## Problem

Two requests to the same endpoint (`/api/idp/initiate`) from different origins produce different cookie behavior — one sets cookies, the other does not.

### Request 1 — Cookie IS set

```
Origin: https://dev-os.blocksdevelopers.com
Referer: https://dev-os.blocksdevelopers.com/
redirectUri: https://dev-monitor.blocksdevelopers.com/login/callback
forwardedTo: /console
```

### Request 2 — Cookie is NOT set

```
Origin: https://dev-monitor.blocksdevelopers.com
Referer: https://dev-monitor.blocksdevelopers.com/
redirectUri: https://dev-monitor.blocksdevelopers.com/login/callback
(no forwardedTo)
```

## Root Cause

Cookies are set during the **callback phase** (`HandleCallbackAsync`), not during initiate. The callback uses `DomainResolver.ResolveDomain()` to decide whether to set cookies.

### How domain resolution works

```
IdpService.cs:246
var (domain, cookieDomain, isResolved) = DomainResolver.ResolveDomain(tenant, httpRequest);
```

```
IdpService.cs:260-263
if (isResolved && !string.IsNullOrWhiteSpace(domain))
{
    AppendCookies(tokenResponseObj, httpResponse, domain);
}
```

`ResolveDomain` does the following:

1. Calls `BlocksContext.ResolveApplicationDomain(request)` — extracts the domain from the request's **Origin** or **Referer** header.
2. Calls `FindDomainMatch(domains, effectiveContextDomain)` — matches the extracted domain against the tenant's configured `Applications[].Domain` list.
3. Returns `isResolved = true` only if a match is found.

### Why Request 1 works and Request 2 does not

The tenant's `Applications` configuration has `dev-os.blocksdevelopers.com` registered as a domain but **does not** have `dev-monitor.blocksdevelopers.com`.

| | Request 1 | Request 2 |
|---|---|---|
| **Origin header** | `dev-os.blocksdevelopers.com` | `dev-monitor.blocksdevelopers.com` |
| **Domain match found?** | Yes | No |
| **isResolved** | `true` | `false` |
| **Cookies set?** | Yes | No |

### Different response shapes

When `isResolved = true` (cookies set):

```json
{
  "id_token": "...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "..."
}
```

Tokens are in cookies — only `id_token` is in the response body.

When `isResolved = false` (no cookies):

```json
{
  "access_token": "...",
  "refresh_token": "...",
  "id_token": "...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "scope": "..."
}
```

All tokens are returned in the response body since no cookies were set.

## Fix

Add `dev-monitor.blocksdevelopers.com` to the tenant's `Applications` configuration with the appropriate `CookieDomain` (e.g., `.blocksdevelopers.com`).

## Secondary Issue: SameSite Cookie Attribute

In `DomainResolver.CreateCookieOptions` (line 244):

```csharp
SameSite = isLocal ? SameSiteMode.None : SameSiteMode.Strict,
```

The comment directly above this line (lines 239-243) explains this is a **cross-origin SSO flow** where `SameSite=None` is needed:

> This is a cross-origin SSO flow: the SPA fetches the IDP callback
> from a different origin than the IDP itself, so the auth/refresh
> cookies must be SameSite=None (which mandates Secure, set above).
> SameSite=Strict would stop the browser from accepting/sending them
> on the cross-site flow.

But the code sets `SameSite=Strict` in production — the opposite of what the comment says. This means even when cookies ARE set, the browser will **not send them** on cross-site requests (e.g., `dev-os` calling `dev-iam`), which can break the SSO flow.

### Relevant code paths

| File | Line | Purpose |
|---|---|---|
| `IdpService.cs` | 52 | `StartAuthenticationFlowAsync` — initiate endpoint |
| `IdpService.cs` | 116 | `HandleCallbackAsync` — callback endpoint, sets cookies |
| `IdpService.cs` | 246 | `ResolveDomain` call |
| `IdpService.cs` | 260-263 | Cookie-setting gate (`isResolved` check) |
| `IdpService.cs` | 324-352 | `AppendCookies` method |
| `DomainResolver.cs` | 66-87 | `ResolveDomain` method |
| `DomainResolver.cs` | 159-188 | `FindDomainMatch` — domain matching logic |
| `DomainResolver.cs` | 225-248 | `CreateCookieOptions` — cookie configuration |
