# Frontend authentication

How the Next.js app signs users in against the SimGuard API (issue #14). The
backend side (Identity, JWT, MFA, lockout, the role matrix) is described in
`backend/docs/auth.md`; this page covers only what the frontend adds.

## Design

The browser never sees, stores or sends a JWT.

```
browser  --cookie-->  Next.js route handlers  --Bearer token-->  ASP.NET API
         <--JSON----                          <--JSON-----------
```

- Route handlers under `src/app/api/` are a thin proxy. They call the backend
  at `BACKEND_URL` (a server-only variable), keep the token in an httpOnly
  cookie and return small JSON bodies to the browser.
- Screens call `/api/backend/...` (through `src/lib/api.js`); the forwarding
  route adds the token from the cookie.
- Tokens are never put in `localStorage`, `sessionStorage`, URLs or logs.
- Every state-changing route handler rejects cross-origin requests: the
  `Origin` header must match the request host (without an `Origin`, only
  `Sec-Fetch-Site: same-origin` is accepted).
- The `next` redirect parameter is accepted only as a relative same-origin
  path (`safeNextPath` in `src/lib/auth/core.js`): no scheme, no `//`, no
  backslash, no control characters, nothing under `/api` or `/login`. After
  sign-in a `next` page the role may not open is replaced by the role's home.
- The backend enforces every permission. The frontend map only shapes the UI.

## Cookies

| Cookie       | Holds                                         | Lifetime                                 |
| ------------ | --------------------------------------------- | ---------------------------------------- |
| `sg_session` | the access token                              | until the token's `expiresAt`, at most 60 minutes |
| `sg_mfa`     | `challenge:<token>` or `setup:<token>`, the short-lived MFA token between password and code | until its `expiresAt`, at most 5 minutes |

Both are `HttpOnly`, `SameSite=Lax`, `Path=/`, and `Secure` unless the host
is `localhost`, `127.0.0.1` or `[::1]`. `Max-Age` is the smaller of the cap
and the time left on the token, so a cookie never outlives its token. Setting
one cookie deletes the other: a pending sign-in has no session, and a session
has no pending token. Sign-out deletes both.

## Sign-in flow

1. `/login`: email and password go to `POST /api/auth/login`. The backend
   answers with an MFA token and whether the user must verify or enrol; the
   route stores it in `sg_mfa` and returns `{ next: "mfa" | "mfa-setup" }`.
2. `/login/mfa`: a 6-digit app code, or a recovery code
   (`?method=recovery`), goes to `POST /api/auth/mfa/verify`.
3. `/login/mfa-setup` (first sign-in): `POST /api/auth/mfa/enroll/start`
   returns the key and `otpauth://` URI, drawn as a QR code (`uqr`, no network
   call). The confirm code goes to `/api/auth/mfa/enroll/confirm`, which
   returns the 10 recovery codes once. They can be copied or downloaded, and
   the user must tick "I have saved these" to continue.
4. `/login/change-password`: shown while the account has
   `mustChangePassword` (seeded and admin-created accounts). The rules (at
   least 12 characters, at least 4 different characters, different from the
   current one) are checked live. The new token replaces the session.
5. The user lands on `next` if it is safe and allowed for the role, else on the
   role's home: `/admin` for Admin, `/dashboard` for Security Officer and
   Auditor.

Wrong, locked and disabled accounts get the same "Email or password is
incorrect" message, with a note that 5 failed tries lock sign-in for 15
minutes; the backend gives no way to tell them apart. A missing or expired
`sg_mfa` sends the user back to `/login?reason=restart`.

## Route handlers

All return JSON. Errors have one shape, `{ "error": { "code": "...",
"fields"?: {...}, "retryAfter"?: seconds } }`, with the backend's HTTP status
where there is one.

| Route                                 | Method | Needs        | Returns                                   |
| ------------------------------------- | ------ | ------------ | ----------------------------------------- |
| `/api/auth/login`                     | POST   | -            | `{ next }`, sets `sg_mfa`                 |
| `/api/auth/mfa/verify`                | POST   | `sg_mfa` challenge | `{ role, mustChangePassword }`, sets `sg_session` |
| `/api/auth/mfa/enroll/start`          | POST   | `sg_mfa` setup | `{ sharedKey, authenticatorUri }`       |
| `/api/auth/mfa/enroll/confirm`        | POST   | `sg_mfa` setup | `{ recoveryCodes, role, mustChangePassword }`, sets `sg_session` |
| `/api/auth/change-password`           | POST   | `sg_session` | `{ role }`, replaces `sg_session`         |
| `/api/auth/logout`                    | POST   | -            | 204, deletes both cookies                 |
| `/api/auth/me`                        | GET    | `sg_session` | `{ email, name, role, mustChangePassword }` |
| `/api/backend/<path>`                 | any    | `sg_session` | the backend's response, passed through    |

Error codes: `invalid_credentials`, `mfa_invalid`, `challenge_expired`,
`rate_limited`, `validation`, `no_role`, `unauthorized`, `forbidden`,
`not_found`, `conflict`, `cross_origin`, `network`, `server`.

### The forwarding route

`/api/backend/[...path]` sends `GET`, `POST`, `PUT`, `PATCH` and `DELETE` to
`BACKEND_URL/api/<path>` with the query string, the raw body, `Accept`,
`Content-Type` and `Authorization: Bearer <token from sg_session>`. The status
and body come back unchanged (problem details included), with the
`Content-Type`, `Content-Disposition`, `Location` and `Retry-After` headers.

- No session cookie: 401 `{ error: { code: "unauthorized" } }`.
- Backend 401: the session cookie is deleted and the 401 is passed on.
- Backend down or timed out (30 s; the `/api/auth` routes wait 15 s): 502
  `{ error: { code: "network" } }`.
- Cross-origin write: 403 `{ error: { code: "cross_origin" } }`.
- Paths with `..` or empty segments, and `auth/*` except four routes that
  return no token, are refused with 404 `{ error: { code: "not_found" } }`, so
  tokens can only be issued through the routes above. The four are
  `GET auth/events` (the auth event log), `PUT auth/me` (own name),
  `POST auth/mfa/recovery-codes` and `POST auth/me/delete` (see Profile below).
  The list is `FORWARDED_AUTH_ROUTES` in `src/lib/auth/core.js`.

`src/lib/api.js` uses it: requests go to `/api/backend/...` with
`credentials: "same-origin"`; a 401 sends the browser to
`/login?next=<current path>&reason=expired`; a `network` body becomes an
`ApiError` with status 0; other errors become `ApiError` with the backend's
status and field `errors`.

## Pages and sessions

- `src/proxy.js` sends a page request without `sg_session` to
  `/login?next=<path>` before anything renders, and sends `/login/mfa` or
  `/login/mfa-setup` without the matching `sg_mfa` to `/login?reason=restart`.
- `src/app/(app)/layout.jsx` checks the session with the backend
  (`GET /api/auth/me`) on every request. A rejected token deletes the cookie
  and goes to `/login?reason=expired`; `mustChangePassword` goes to
  `/login/change-password`. It sets `instant = false` and blocks instead of
  streaming (a Suspense boundary would fix the status of `/sims/[id]` at 200).
- `AuthProvider` gives client components `useAuth()`:
  `{ email, name, role, refresh, signOut }`.
- Sign-out (account menu) calls `/api/auth/logout`, then loads
  `/login?reason=signedOut`.

In development, Next.js logs "Could not validate that a segment in your UI has
instant navigation" for the pages under `(app)`: the layout reads cookies, and
its `instant = false` does not cover the pages below it. The messages are
development-only and do not affect the build; adding `export const instant =
false` to each page silences them.

## Permissions map

`src/lib/permissions.js` mirrors the backend role matrix for the UI only.

| Page                 | Admin | Security Officer | Auditor |
| -------------------- | :---: | :--------------: | :-----: |
| Admin area           |  yes  |                  |         |
| Dashboard            |       |       yes        |   yes   |
| SIMs, Allocations    |       |       yes        |   yes   |
| Alerts, Usage        |       |       yes        |   yes   |
| Settings: Carriers tab |       |       yes        |   yes   |
| Settings: Profile tab  |  yes  |       yes        |   yes   |
| Imports              |       |       yes        |         |
| Tier recommendations |       |       yes        |         |
| Audit log            |  yes  |                  |   yes   |

| Action                                                                 | Allowed for      |
| ---------------------------------------------------------------------- | ---------------- |
| `sims.edit`, `sims.changeStatus`, `sims.changeTier`, `allocations.edit`, `alerts.review`, `carriers.edit`, `departments.edit`, `employees.edit`, `imports.run`, `tierRecommendations.edit`, `settings.edit` | Security Officer |
| `auditLog.view`                                                        | Admin, Auditor   |
| `users.manage`                                                         | Admin            |
| `scoring.run` (Refresh scores on the Dashboard)                        | Security Officer |

- `navItemsFor(role)` filters the menu; the SIM search shows only for roles
  that may open SIMs.
- `RouteGuard` (in the app layout) shows "You do not have access to this page"
  with a link to the role's home for a page outside the role. Paths not in
  the map are not guarded.
- `can(role, action)` and `deniedReason(role, action)` are for disabled
  buttons with a reason ("Auditors have read-only access").

## Profile (Settings, Profile tab)

Every role has it; the Admin sees only this tab. It holds four sections.

- **Your details:** name (editable, `PUT auth/me`), email and role (read only).
- **Password:** the existing `/api/auth/change-password` route handler; the
  new token replaces the session, so the user stays signed in.
- **Two-factor:** shows that it is on (it cannot be turned off) and makes 10
  new recovery codes after the password and a current 6 digit code
  (`POST auth/mfa/recovery-codes`). The codes are shown once in a dialog that
  does not close on Escape or an outside click; "Copy codes" and "I have saved
  them" are its buttons.
- **Delete my account** (separate danger area): a dialog lists what happens and
  says it cannot be undone, then asks for the password and a current code
  (`POST auth/me/delete`). On success the session cookies are dropped and the
  browser does a full page load of the landing page. A wrong password or code
  shows on the field (400); a 409 shows the last Admin message. An Admin gets
  the button disabled with the reason in plain text next to it.

The password and code dialog is `confirm-identity-dialog.jsx`: Radix Dialog
(focus is trapped and returns to the button that opened it, Escape and Cancel
close it), the password field first, the error summary takes focus after a
failed try. Its text is in `copy.settings.profile`.

## Running it locally

1. Start the API (see `backend/README.md`) on `http://localhost:5275`, with
   the first Admin from `SimGuard:Seed:AdminEmail` and
   `SimGuard:Seed:AdminPassword`. In Development, `SimGuard:Seed:OfficerEmail`
   / `OfficerPassword` and `AuditorEmail` / `AuditorPassword` seed one
   Security Officer and one Auditor. Keep these in user-secrets or
   environment variables, never in the repository.
2. In `frontend/`: `cp .env.example .env.local`, set `BACKEND_URL` if the API
   is elsewhere, then `npm run dev`.
3. Open http://localhost:3000/login. The first sign-in of each seeded account
   asks for MFA enrolment (any TOTP app) and a new password.

Sign-in is rate limited by the backend per client IP. Behind the Next.js
proxy every user shares the server's IP, so the limit is shared too.
