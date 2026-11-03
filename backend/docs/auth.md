# Authentication and authorization

SimGuard API uses ASP.NET Core Identity (EF Core store, PostgreSQL) for
accounts and JWT bearer tokens for API access. Every account must use TOTP
multi-factor authentication. There is one organisation; Admins create all
accounts (no self-registration).

## Sign-in flow

```
first sign-in (new account or after an MFA reset)
  client                                API
  POST /api/auth/login {email,password}  -> 200 {status:"mfaEnrollmentRequired", mfaToken}
  POST /api/auth/mfa/enroll/start         -> 200 {sharedKey, authenticatorUri}   (Bearer mfaToken)
  (user scans the QR code drawn from authenticatorUri)
  POST /api/auth/mfa/enroll/confirm {code} -> 200 {recoveryCodes[10], accessToken, mustChangePassword}
                                              (Bearer mfaToken)
  if mustChangePassword:
  POST /api/auth/change-password          -> 200 {accessToken}  (Bearer accessToken)

normal sign-in
  POST /api/auth/login {email,password}  -> 200 {status:"mfaRequired", mfaToken}
  POST /api/auth/mfa/verify {mfaToken, code} -> 200 {accessToken}
```

- The `mfaToken` is valid for 5 minutes. It has its own audience and is
  never accepted as an access token.
- The access token is valid for 60 minutes. Claims: `sub` (user id),
  `email`, `role`, plus `jti` and `sst` (a hash of the Identity security
  stamp).
- While `mustChangePassword` is true, the access token works only for
  `GET /api/auth/me` and `POST /api/auth/change-password`; everything else
  returns 403.
- A token stops working immediately (401) when the account is disabled, its
  role changes, its password changes or is reset, or its MFA is reset. The
  API checks the account on every request.
- There is no refresh token. When the access token expires, the user signs
  in again (see "Out of scope").

## Errors

All errors use ProblemDetails (`application/problem+json`), as in the rest
of the API. Validation errors are 400 with an `errors` object keyed by field.

| Status | Meaning |
| ------ | ------- |
| 400 | Validation failed (missing field, password policy, wrong enrolment code) |
| 401 | No token, invalid or expired token, wrong credentials, wrong MFA code, locked or disabled account |
| 403 | Signed in, but the role (or a pending password change) does not allow this endpoint |
| 404 | User not found |
| 409 | Conflict (duplicate email, employee already linked, self-demotion, last Admin) |
| 429 | Too many requests to the sign-in endpoints |

Wrong password, unknown email, disabled account and locked account all
return the same 401 `"Invalid email or password."`, so the response does not
reveal whether an account exists or is locked.

## Endpoint contract

Base path: `/api`. Timestamps are UTC ISO 8601.

### POST /api/auth/login

Anonymous, rate limited.

```json
{ "email": "admin@simguard.local", "password": "correct horse battery" }
```

200:

```json
{
  "status": "mfaRequired",
  "mfaToken": "eyJhbGciOi...",
  "expiresAt": "2026-11-03T10:05:00Z"
}
```

`status` is `mfaRequired` (call `/mfa/verify`) or `mfaEnrollmentRequired`
(call `/mfa/enroll/start` with the token as `Authorization: Bearer`).
Errors: 400, 401, 429.

### POST /api/auth/mfa/enroll/start

`Authorization: Bearer <mfaToken from login with mfaEnrollmentRequired>`.
No body. Each call creates a new authenticator key.

200:

```json
{
  "sharedKey": "jbsw y3dp ehpk 3pxp ...",
  "authenticatorUri": "otpauth://totp/SimGuard%3Aadmin%40simguard.local?secret=JBSWY3DPEHPK3PXP...&issuer=SimGuard&digits=6&period=30"
}
```

Errors: 401 (missing, expired or wrong kind of token, or MFA already enabled), 429.

### POST /api/auth/mfa/enroll/confirm

`Authorization: Bearer <same enrolment mfaToken>`.

```json
{ "code": "123456" }
```

200 (the recovery codes are shown only here, once):

```json
{
  "recoveryCodes": ["k7m2q-9xw4p", "..."],
  "accessToken": "eyJhbGciOi...",
  "tokenType": "Bearer",
  "expiresAt": "2026-11-03T11:00:00Z",
  "mustChangePassword": true
}
```

Errors: 400 (`code` wrong; counts as a failed attempt), 401, 409 (enrolment
not started), 429.

### POST /api/auth/mfa/verify

Anonymous, rate limited. `code` is the 6-digit TOTP code or one unused
recovery code (each recovery code works once).

```json
{ "mfaToken": "eyJhbGciOi...", "code": "123456" }
```

200:

```json
{
  "accessToken": "eyJhbGciOi...",
  "tokenType": "Bearer",
  "expiresAt": "2026-11-03T11:00:00Z",
  "mustChangePassword": false
}
```

Errors: 400, 401 (`"The MFA token or code is invalid or has expired."`), 429.

### GET /api/auth/me

Any access token, including one with a pending password change.

200:

```json
{
  "id": 1,
  "email": "admin@simguard.local",
  "name": "Administrator",
  "role": "Admin",
  "employeeId": null,
  "mfaEnabled": true,
  "mustChangePassword": false
}
```

### POST /api/auth/change-password

Any access token, including one with a pending password change. Rate limited.

```json
{ "currentPassword": "old password", "newPassword": "a new long passphrase" }
```

200: same shape as `/mfa/verify` with `mustChangePassword: false`. The old
token stops working; use the new one.
Errors: 400 (`currentPassword` incorrect, which counts as a failed attempt,
or `newPassword` fails the policy or equals the current one), 401, 429.

### GET /api/auth/events

Admin or Auditor. Query: `type`, `from`, `to` (ISO 8601 with offset, e.g.
`2026-11-03T00:00:00Z`), `userId`, `page` (default 1), `pageSize` (default 20,
max 100). Newest first.

200:

```json
{
  "items": [
    {
      "id": 42,
      "userId": 3,
      "emailAttempted": "officer@simguard.local",
      "eventType": "login_failed",
      "success": false,
      "ipAddress": "127.0.0.1",
      "userAgent": "Mozilla/5.0 ...",
      "createdAt": "2026-11-03T10:01:02.123456Z"
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 1
}
```

Event types: `login_succeeded`, `login_failed`, `lockout`, `mfa_failed`,
`mfa_enrolled`, `mfa_reset`, `recovery_code_used`, `password_changed`,
`password_reset`, `user_created`, `role_changed`, `user_disabled`,
`user_enabled`. `userId` is null when the email did not match an account.
Passwords, tokens and codes are never stored.

### User administration (Admin only)

`UserResponse`, returned by every endpoint below:

```json
{
  "id": 3,
  "email": "officer@simguard.local",
  "name": "Jane Wanjiru",
  "role": "SecurityOfficer",
  "employeeId": 12,
  "mfaEnabled": false,
  "mustChangePassword": true,
  "isDisabled": false,
  "lockedOutUntil": null,
  "createdAt": "2026-11-03T09:00:00Z"
}
```

| Method and path | Body | Success | Errors |
| --------------- | ---- | ------- | ------ |
| `GET /api/users?role=&page=&pageSize=` | | 200 `PagedResult<UserResponse>` | 400 |
| `GET /api/users/{id}` | | 200 | 404 |
| `POST /api/users` | `{ "email", "name", "role", "employeeId": null, "temporaryPassword" }` | 201, `Location: /api/users/{id}` | 400, 409 |
| `PUT /api/users/{id}/role` | `{ "role": "Auditor" }` | 200 | 400, 404, 409 |
| `POST /api/users/{id}/disable` | | 200 | 404, 409 |
| `POST /api/users/{id}/enable` | | 200 | 404 |
| `POST /api/users/{id}/mfa/reset` | | 200 | 404 |
| `POST /api/users/{id}/password/reset` | `{ "temporaryPassword": "..." }` | 200 | 400, 404 |

- New accounts and reset passwords have `mustChangePassword = true`.
- `employeeId` is optional and links the account to one Employee (one
  account per employee).
- An Admin cannot change their own role or disable themselves (409).
- The last active Admin cannot be demoted or disabled (409). These checks
  run under a database lock, so two Admins cannot remove each other at once.
- Password reset also clears a lockout.
- MFA reset removes the authenticator key and recovery codes; the user
  enrols again at the next sign-in.
- There is no delete; disable the account instead.

## Role matrix

| Endpoint | Admin | SecurityOfficer | Auditor | Policy |
| -------- | :---: | :-------------: | :-----: | ------ |
| `GET /`, `POST /api/auth/login`, `POST /api/auth/mfa/verify` | anonymous | anonymous | anonymous | none |
| `POST /api/auth/mfa/enroll/*` | enrolment token | enrolment token | enrolment token | `MfaEnrollment` |
| `GET /api/auth/me`, `POST /api/auth/change-password` | yes | yes | yes | `SignedIn` |
| `GET /health/database` | yes | yes | yes | `CanRead` |
| `GET` departments, employees, SIMs, allocations | yes | yes | yes | `CanRead` |
| `POST/PUT/DELETE /api/departments` | yes | no | no | `CanManageOrganisation` |
| `POST/PUT/DELETE /api/employees` | yes | no | no | `CanManageOrganisation` |
| `POST/PUT/DELETE /api/sims` | yes | no | no | `CanManageSims` |
| `PATCH /api/sims/{id}/status` | yes | yes | no | `CanChangeSimStatus` |
| `POST /api/allocations`, `POST /api/allocations/{id}/return` | yes | yes | no | `CanManageAllocations` |
| `/api/users/*` | yes | no | no | `CanManageUsers` |
| `GET /api/auth/events` | yes | no | yes | `CanReadAudit` |
| `GET` alerts, risk scores, SIM risk and activity, dashboard summary | yes | yes | yes | `CanRead` |
| `PATCH /api/alerts/{id}/status` | yes | yes | no | `CanReviewAlerts` |
| `GET /api/audit-logs` | yes | no | yes | `CanReadAudit` |
| `POST /api/dev/seed-demo` (Development only) | yes | no | no | `CanUseDevTools` |

Policies are defined in `SimGuard.Api/Common/Authorization/AuthPolicies.cs`.
A fallback policy requires a full access token for any endpoint that does
not declare its own policy, so a new endpoint is protected by default.

## Security settings

| Setting | Value |
| ------- | ----- |
| Password | at least 12 characters, at least 4 distinct characters, no composition rules |
| Lockout | 5 failed attempts (password, MFA code or current password on change), then 15 minutes |
| Rate limit | 10 requests per minute per client IP on login, MFA and change-password (configurable) |
| Access token | HS256, 60 minutes, clock skew 30 seconds |
| MFA token | 5 minutes |
| CORS | origins from `Cors:AllowedOrigins` only, credentials allowed, no wildcard |

## Configuration keys

| Key | Required | Default | Notes |
| --- | -------- | ------- | ----- |
| `ConnectionStrings:DefaultConnection` | yes | | PostgreSQL |
| `Jwt:Key` | yes outside Development | random per start in Development | at least 32 bytes; the API refuses to start without it outside Development |
| `Jwt:Issuer` | no | `SimGuard` | |
| `Jwt:Audience` | no | `simguard-api` | |
| `Jwt:AccessTokenMinutes` | no | `60` | |
| `Jwt:MfaTokenMinutes` | no | `5` | |
| `Jwt:ClockSkewSeconds` | no | `30` | |
| `Cors:AllowedOrigins` | no | `["http://localhost:3000"]` | array; `*` is rejected at startup |
| `RateLimiting:Auth:PermitLimit` | no | `10` | |
| `RateLimiting:Auth:WindowSeconds` | no | `60` | |
| `SimGuard:Seed:AdminEmail` | first run only | | |
| `SimGuard:Seed:AdminPassword` | first run only | | at least 12 characters |
| `SimGuard:Seed:AdminName` | no | `Administrator` | |

Keep secrets out of `appsettings*.json`. In Development use user-secrets;
elsewhere use environment variables (`Jwt__Key`, `SimGuard__Seed__AdminPassword`,
`Cors__AllowedOrigins__0`, ...).

```bash
cd backend/SimGuard.Api
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>"
```

## Seeding the first Admin

At startup the API creates the roles `Admin`, `SecurityOfficer` and
`Auditor` if they are missing. If there are no accounts at all, it creates
the first Admin from `SimGuard:Seed:AdminEmail` and
`SimGuard:Seed:AdminPassword`. The password is never logged.

```bash
cd backend/SimGuard.Api
dotnet ef database update
dotnet user-secrets set "SimGuard:Seed:AdminEmail" "admin@simguard.local"
dotnet user-secrets set "SimGuard:Seed:AdminPassword" "<temporary password, 12+ characters>"
dotnet run
```

The first sign-in requires MFA enrolment and then a password change. After
the Admin exists, remove the seed password:

```bash
dotnet user-secrets remove "SimGuard:Seed:AdminPassword"
```

## Database

Migration `AddAuthentication` adds:

- `app_users`, `app_roles`, `app_user_roles`, `app_user_claims`,
  `app_user_logins`, `app_user_tokens`, `app_role_claims` (Identity, snake_case
  columns). `app_users.employee_id` is a nullable, unique link to `employees`.
- `auth_events` (`id`, `user_id`, `email_attempted`, `event_type`, `success`,
  `ip_address`, `user_agent`, `created_at`).

`audit_logs.user_id` is unchanged and still points to `employees`.

## Smoke test

`backend/docs/auth-smoke.ps1` runs the whole flow against a running API and
prints each step's expected and real status code. See the header of the
script for how to run it.

## Out of scope

- Refresh tokens (follow-up: short access token plus a rotating, revocable
  refresh token in an HttpOnly cookie).
- OIDC / external identity providers, SAML.
- Multi-tenancy (one organisation only).
- Self-registration and email-based password reset.
- SMS or email MFA (TOTP only).
- Recording the acting user on SIM and allocation changes in `audit_logs`
  (issue #13).

## Known limitations

- Identity stores the authenticator key and the recovery codes in
  `app_user_tokens` in plain text. Protect database access and backups.
- A TOTP code can be used more than once within its 30 second window.
- The rate limiter and lockout use the client IP as seen by the API. Behind
  a reverse proxy, configure forwarded headers first.
