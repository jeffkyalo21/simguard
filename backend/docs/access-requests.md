# Access requests

A visitor asks for an account from the landing page ("Register"). The
request is stored; an Admin reads it in the Admin screen and approves or
declines it. Approving creates the account at once, with the requested role
and a random password that is emailed to the person. The Admin never types
or sees that password. If the email fails, the Admin uses "resend sign-in
email" (`POST /api/users/{id}/signin-email`, see [auth.md](auth.md)).

## Endpoints

| Method and path | Policy | Success | Errors |
| --------------- | ------ | ------- | ------ |
| `POST /api/access-requests` | anonymous, rate limited | 202 `{ "received": true }` | 400, 429 |
| `GET /api/access-requests?status=&page=&pageSize=` | `AdminOnly` | 200 `PagedResult<AccessRequest>`, newest first | 400, 401, 403 |
| `PATCH /api/access-requests/{id}/status` | `AdminOnly` | 200 `AccessRequest` | 400, 401, 403, 404, 409 |

## Sending a request

```json
{ "name": "Amina Odhiambo", "email": "amina@example.com", "organisation": "Acme Ltd",
  "role": "SecurityOfficer", "note": "We have 300 SIMs." }
```

- `name` (at most 100), `email` (valid, at most 256) and `organisation`
  (at most 150) are required. `note` is optional, at most 500.
- `role` is `SecurityOfficer` or `Auditor` (any casing). Nobody can request
  `Admin`.
- The email is stored in lower case. There is at most one pending request
  per email. A second request from the same email while one is pending is
  answered exactly like a new one (202, same body) and stores nothing, so
  the response never reveals what exists. Once a request is approved or
  declined, the same email can ask again.
- The response does not say whether the email already has an account.
- Rate limit: 5 requests per hour per client address
  (`RateLimiting:AccessRequests:PermitLimit` and `WindowSeconds`). Over the
  limit gives 429. The web site calls this API for every visitor and sends
  the visitor's address in `X-Forwarded-For`. The API uses it only when the
  call comes from loopback or from an address listed in
  `ForwardedHeaders:KnownProxies` (an array of IPs); otherwise every visitor
  would share one limit. The same address is used for the sign-in limit.

## AccessRequest

```json
{ "id": 4, "name": "Amina Odhiambo", "email": "amina@example.com", "organisation": "Acme Ltd",
  "role": "SecurityOfficer", "note": "We have 300 SIMs.", "status": "pending",
  "createdAt": "2026-11-08T05:10:00Z", "reviewedAt": null, "reviewedByEmail": null }
```

`status` is `pending`, `approved` or `declined`.

## Reviewing

`PATCH /api/access-requests/{id}/status` with `{ "status": "approved" }` or
`{ "status": "declined" }`. Only a pending request can be reviewed (else
409).

Approving, in one transaction with the decision:

- If no account has the request's email, one is created: role as requested,
  `mustChangePassword = true`, a random 20 character password, no employee
  link. The audit entry `user.created` is written too (with
  `accessRequestId`). The password is emailed (see Email) and is never
  stored, returned or audited.
- If an account with that email already exists, nothing is created and its
  password is not touched. The request is still marked approved.
- If the account cannot be created, the request stays pending.

The review response adds `accountCreated`: `true`, `false` (account already
existed), or `null` (declined).

The body may carry `"temporaryPassword"` only when the API runs in
Development, so the test scripts can sign in to the new account. In any other
environment it is a 400 (field `temporaryPassword`) and nothing changes.

The review writes the audit entry
`user.access_request_reviewed` (category `system`, so the Admin sees it)
with `oldValues` `{ accessRequestId, status }` and `newValues`
`{ accessRequestId, status, email, role }`.

## Email

Sent through Brevo's transactional API (`POST /v3/smtp/email`), from
`no-reply@sim-guard.app` by default (the domain must be verified in
Brevo). Three emails:

| When | To | Content |
| ---- | -- | ------- |
| A new request is stored | the visitor | request received (not sent for a repeated pending email) |
| `approved` and the account was created | the visitor | sign-in link, email and the generated temporary password. The same email is sent by an invitation and by "resend sign-in email". The password is never stored or audited |
| `declined` | the visitor | short notice, no reason |

An email never fails the request. The review response gets an additive
`emailSent`: `true` or `false` when an email was due, `null` when not (an
existing account was approved). `false` means the account exists but the
person has no password yet: the Admin uses "resend sign-in email".
Without `Brevo:ApiKey` nothing is sent and `emailSent` is `false`.

| Setting | Default | Note |
| ------- | ------- | ---- |
| `Brevo:ApiKey` | none | user-secrets or `Brevo__ApiKey`; never committed |
| `Brevo:SenderEmail` | `no-reply@sim-guard.app` | verified sender in Brevo |
| `Brevo:SenderName` | `SimGuard` | |
| `Brevo:SignInUrl` | `http://localhost:3000/login` | link in the account-ready email; set it to the real site |
| `Brevo:BaseUrl` | `https://api.brevo.com` | override only to test against a stub |
| `Brevo:TimeoutSeconds` | 10 | |

The received email goes out in the background, so the form answers at the
same speed for a new and a repeated email. Each address can still trigger
one email per pending request, within the rate limit above.

## Smoke test

[access-requests-smoke.ps1](access-requests-smoke.ps1) checks this contract
against a running Development API on a throwaway database without a Brevo key. Start the API with
`RateLimiting__Auth__PermitLimit=1000` and
`RateLimiting__AccessRequests__PermitLimit=1000`, then run it with the same
credentials setup as [roles-smoke.ps1](roles-smoke.ps1). Run it again with
`-RateLimitOnly` against an API that uses the default limits.

For the full flow with the emails checked, start
[email-stub.ps1](email-stub.ps1) on a free port, start the API with
`Brevo__ApiKey=test-key` and `Brevo__BaseUrl=http://localhost:<port>`, and
run the smoke script with `-EmailStubLog <the stub's log file>`. It signs in
to a request-approved account using the password read from the stub's log,
then checks "resend sign-in email" replaces it.

Run it with `-NotDevelopment` against an API started in another environment
(for example `ASPNETCORE_ENVIRONMENT=Production`) to check the Admin cannot
choose a password there.
