# IdP Session (SSO) – Backend README

## Overview

The IdP session is a **browser-based, server-managed container** that holds multiple authenticated accounts.

```text
Account = (user_id + tenant_id)
Session = collection of accounts
```

---

## Session Structure

```json
{
  "session_id": "S1",
  "accounts": [
    { "user_id": "u1", "tenant_id": "TenantA" },
    { "user_id": "u2", "tenant_id": "TenantB" }
  ],
  "idle_expiry": "...",
  "absolute_expiry": "..."
}
```

---

## Session Validity

```text
Session is valid if:
  now < idle_expiry
  AND
  now < absolute_expiry
```

---

## Session Lifecycle

### On Request (`/authorize` or any IdP interaction)

1. Load session via cookie
2. Validate expiry
3. If valid:

   * Update:

     ```text
     idle_expiry = now + idle_timeout
     ```

---

### On Login (Password / Social)

```text
IF session exists:
  add (user_id + tenant_id) to accounts
ELSE:
  create new session and add account

Update idle_expiry
```

---

### On Logout (Single Account)

```text
Remove (user_id + tenant_id) from accounts
```

* If accounts empty → delete session
* Else → session remains valid

---

### On Global Logout

```text
Delete session
Clear cookie
Remove all accounts
```

---

### On Session Expiry

```text
Session deleted
All accounts removed
SSO no longer valid
```

---

## Account Resolution Logic

```text
accounts = session.accounts

IF tenant_id provided:
  filter accounts by tenant_id

IF exactly 1:
  use it

IF multiple:
  require explicit selection

IF none:
  require login
```

---

## Session Rotation

```text
DO NOT rotate session_id on each request
```

Rotate only when:

* After login (optional, for security)
* After sensitive changes
* On suspicious activity

---

## Notes

* Session is **stateful and stable**
* Supports **multiple accounts across tenants**
* Each request must resolve to **one (user + tenant)**
* No need for `active_account` (resolution is deterministic)

---

## Summary

```text
IdP session = stable container of accounts
Validated by idle + absolute expiry
Updated on activity
Accounts added/removed dynamically
No per-request rotation required
```
