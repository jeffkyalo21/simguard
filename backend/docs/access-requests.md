# Access requests

A visitor asks for an account from the landing page ("Register"). The
request is stored; an Admin reads it in the Admin screen and creates the
account by hand with `POST /api/users` (see [auth.md](auth.md)). Nothing is
emailed: the Admin passes the temporary password on.

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
  limit gives 429.

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
409). Approving does not create the account: it records that the Admin
dealt with the request. The review writes the audit entry
`user.access_request_reviewed` (category `system`, so the Admin sees it)
with `oldValues` `{ accessRequestId, status }` and `newValues`
`{ accessRequestId, status, email, role }`.

## Smoke test

[access-requests-smoke.ps1](access-requests-smoke.ps1) checks this contract
against a running Development API on a throwaway database. Start the API with
`RateLimiting__Auth__PermitLimit=1000` and
`RateLimiting__AccessRequests__PermitLimit=1000`, then run it with the same
credentials setup as [roles-smoke.ps1](roles-smoke.ps1). Run it again with
`-RateLimitOnly` against an API that uses the default limits.
