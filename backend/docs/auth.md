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
`user_enabled`, `user_unlocked`. `userId` is null when the email did not match an account.
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
| `POST /api/users` (invite) | `{ "email", "name", "role", "employeeId": null }` | 201, `Location: /api/users/{id}` | 400, 409 |
| `PUT /api/users/{id}/role` | `{ "role": "Auditor" }` | 200 | 400, 404, 409 |
| `POST /api/users/{id}/disable` | | 200 | 404, 409 |
| `POST /api/users/{id}/enable` | | 200 | 404 |
| `POST /api/users/{id}/unlock` | | 200 | 404 |
| `POST /api/users/{id}/mfa/reset` | | 200 | 404 |
| `POST /api/users/{id}/signin-email` | | 200 | 404, 409 |
| `POST /api/users/{id}/password/reset` (Development only) | `{ "temporaryPassword": "..." }` | 200 | 400, 404 |

- The Admin never chooses or sees another person's password. An invite
  (`POST /api/users`) creates the account with a random 20 character password
  and emails it (sign-in link, email, password; see
  [access-requests.md](access-requests.md) for the email setup). Approving an
  access request does the same.
- `UserResponse` has an additive `emailSent` on invite and on
  `signin-email`: `true` when the provider accepted the email, `false` when
  not (then use "resend sign-in email"), `null` on every other call.
- `signin-email` sets a new random password, emails it, sets
  `mustChangePassword = true` and clears any lockout. It works for an account
  that never signed in and for one whose owner forgot the password. The old
  password stops working at once. A disabled account gives 409.
- `unlock` clears a lockout and the failed-attempt count; it does not touch
  the password. Calling it on an unlocked account is a no-op (200).
- `temporaryPassword` on `POST /api/users`, on access request approval and on
  `password/reset` is accepted only when the API runs in Development, so the
  smoke scripts can sign in to accounts they create. In any other environment
  it is a 400 on field `temporaryPassword`, and `password/reset` always
  needs it, so it is unusable there.
- Audit actions: `user.created`, `user.unlocked`, `user.signin_email_sent`
  (all `system`). Auth events: `user_unlocked` is new; a resend logs
  `password_reset`.
- New accounts and passwords set by `signin-email` have `mustChangePassword = true`.
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

The roles split operator from organisation:

- **Admin** is the SimGuard operator. It manages user accounts and reads
  the system audit trail. It does not see or change the organisation's
  data (403 on every organisation endpoint).
- **SecurityOfficer** manages the organisation: departments, employees,
  SIMs, carriers, allocations, SIM status and tier, and alert reviews. It
  cannot manage users or read the audit logs.
- **Auditor** is read-only: every organisation read, the full audit log and
  the auth event log. It never writes.

| Policy | Roles |
| ------ | ----- |
| `OrgRead` | SecurityOfficer, Auditor |
| `OrgWrite` | SecurityOfficer |
| `AdminOnly` | Admin |
| `AuditRead` | Admin, Auditor |
| `AnyRole` | Admin, SecurityOfficer, Auditor |
| `DevTools` | Admin (Development only) |
| `SignedIn` | any full access token, also before the forced password change |
| `MfaEnrollment` | MFA enrolment token |

| Endpoint | Admin | SecurityOfficer | Auditor | Policy |
| -------- | :---: | :-------------: | :-----: | ------ |
| `GET /`, `POST /api/auth/login`, `POST /api/auth/mfa/verify` | anonymous | anonymous | anonymous | none |
| `POST /api/auth/mfa/enroll/start`, `POST /api/auth/mfa/enroll/confirm` | enrolment token | enrolment token | enrolment token | `MfaEnrollment` |
| `GET /api/auth/me`, `POST /api/auth/change-password` | yes | yes | yes | `SignedIn` |
| `GET /health` (database check only, for hosting) | anonymous | anonymous | anonymous | none |
| `POST /api/access-requests` (landing page form, rate limited, see [access-requests.md](access-requests.md)) | anonymous | anonymous | anonymous | none |
| `GET /api/access-requests`, `PATCH /api/access-requests/{id}/status` | yes | no | no | `AdminOnly` |
| `GET /health/database` | yes | yes | yes | `AnyRole` |
| `GET /api/departments`, `/{id}`, `/{id}/employees` | no | yes | yes | `OrgRead` |
| `POST /api/departments`, `PUT/DELETE /api/departments/{id}` | no | yes | no | `OrgWrite` |
| `GET /api/employees`, `/{id}`, `/{id}/allocations` | no | yes | yes | `OrgRead` |
| `POST /api/employees`, `PUT/DELETE /api/employees/{id}` | no | yes | no | `OrgWrite` |
| `GET /api/sims`, `/{id}`, `/{id}/allocations`, `/{id}/risk`, `/{id}/activity`, `/{id}/usage`, `/{id}/usage/cycle` | no | yes | yes | `OrgRead` |
| `POST /api/sims`, `PUT/DELETE /api/sims/{id}` | no | yes | no | `OrgWrite` |
| `PATCH /api/sims/{id}/status` | no | yes | no | `OrgWrite` |
| `PATCH /api/sims/{id}/tier` | no | yes | no | `OrgWrite` |
| `GET /api/carriers` | no | yes | yes | `OrgRead` |
| `POST /api/carriers`, `PUT/DELETE /api/carriers/{id}` | no | yes | no | `OrgWrite` |
| `GET /api/imports`, `GET /api/imports/{type}/template`, `POST /api/imports/{type}` | no | yes | no | `OrgWrite` |
| `GET /api/allocations`, `/{id}` | no | yes | yes | `OrgRead` |
| `POST /api/allocations`, `POST /api/allocations/{id}/return` | no | yes | no | `OrgWrite` |
| `GET /api/alerts`, `/{id}` | no | yes | yes | `OrgRead` |
| `PATCH /api/alerts/{id}/status` | no | yes | no | `OrgWrite` |
| `GET /api/risk-scores` | no | yes | yes | `OrgRead` |
| `GET /api/dashboard/summary` | no | yes | yes | `OrgRead` |
| `GET /api/usage/analytics`, `GET /api/tiers` | no | yes | yes | `OrgRead` |
| `GET /api/tier-recommendations`, `/settings`, `/status` | no | yes | yes | `OrgRead` |
| `PUT /api/tier-recommendations/settings`, `POST /api/tier-recommendations/run`, `POST /api/tier-recommendations/{id}/dismiss` | no | yes | no | `OrgWrite` |
| `POST /api/admin/scoring/run`, `GET /api/admin/scoring/status` (totals only) | yes | no | no | `AdminOnly` |
| `/api/users/*` (list, get, create, role, disable, enable, MFA reset, password reset) | yes | no | no | `AdminOnly` |
| `GET /api/audit-logs` | `system` rows only | no | all rows | `AuditRead` |
| `GET /api/auth/events` | yes | no | yes | `AuditRead` |
| `POST /api/dev/seed-demo` (Development only) | yes | no | no | `DevTools` |

"no" is 403 for a signed-in caller; without a token every endpoint except
the anonymous ones is 401. A token issued before the forced password change
only passes `SignedIn` endpoints (403 elsewhere).

`GET /api/audit-logs` filters by role: Admin sees only rows with `category`
`system` (user management), the Auditor sees every row. See
[api-alerts-risk-audit.md](api-alerts-risk-audit.md).

`POST /api/dev/seed-demo` is the one exception to "Admin sees no
organisation data": it writes demo alerts and risk scores for existing
SIMs. It is a Development tool, mapped only when `ASPNETCORE_ENVIRONMENT`
is `Development`, and does not exist in other environments (404).

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
| `SimGuard:Seed:OfficerEmail`, `OfficerPassword`, `OfficerName` | no (Development only) | name `Security Officer` | test SecurityOfficer |
| `SimGuard:Seed:AuditorEmail`, `AuditorPassword`, `AuditorName` | no (Development only) | name `Auditor` | test Auditor |

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

### Test accounts (Development only)

Admin cannot read or change organisation data, so local testing also needs
a SecurityOfficer and an Auditor. When `ASPNETCORE_ENVIRONMENT` is
`Development`, each startup also creates them from these settings if they
are set and no account has that email yet:

```bash
dotnet user-secrets set "SimGuard:Seed:OfficerEmail" "officer@simguard.local"
dotnet user-secrets set "SimGuard:Seed:OfficerPassword" "<temporary password, 12+ characters>"
dotnet user-secrets set "SimGuard:Seed:AuditorEmail" "auditor@simguard.local"
dotnet user-secrets set "SimGuard:Seed:AuditorPassword" "<temporary password, 12+ characters>"
```

There are no default passwords: an unset email or password means the
account is not created. Like the first Admin, each account must enrol MFA
and change its password at first sign-in (the smoke scripts do this with
`Complete-FirstSignIn`). Outside Development these settings are ignored;
create accounts through `POST /api/users` instead.

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

`backend/docs/roles-smoke.ps1` checks the role matrix: for each role one
endpoint per group (organisation read and write, SIM status and tier
change, users, audit logs, auth events, health) with the expected 2xx or
403, the tier endpoint and the audit log filtering by role. It uses the
Development test accounts above when `SIMGUARD_SMOKE_OFFICER_EMAIL`,
`SIMGUARD_SMOKE_OFFICER_PASSWORD` and the `AUDITOR` equivalents are set,
otherwise it creates throwaway accounts. The alerts and carriers smoke
scripts do every organisation action as a Security Officer.

Run all four on a throwaway database, not on your dev database: auth-smoke
signs in the seeded Admin for the first time and the others leave test
accounts and audit rows behind.

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
