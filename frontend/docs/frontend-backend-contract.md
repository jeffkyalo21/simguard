# Frontend and backend contract

Issue #14, step 5. Every function exported by `src/lib/data.js` compared with
the real API: path, query parameters, response shape, field names, paging
and errors. Screens call `/api/backend/<path without /api>` through
`src/lib/api.js`.

Shared rules that hold for every row below:

- Paged lists are `{ items, page, pageSize, total }` on both sides; `pageSize`
  defaults to 20 and is clamped at 100 by the backend.
- Backend errors are ProblemDetails (`title`, `detail`, `errors` keyed by
  field with messages, not codes). `api.js` turns them into `ApiError` with
  the backend status. The frontend's own routes answer `{ error: { code } }`;
  `api.js` maps `network` to status 0 and a 401 to the sign-in page.
- The backend returns field errors as sentences. Screens map the field
  names to their own lines and use `errorMessage()` for everything else.
- The demo data ends on 2025-03-31. No screen uses the browser clock as
  "now": relative ranges come from the `asOf` date (latest `daily_usage`
  date) that the dashboard and usage responses report.

Decision legend: **frontend** = change the frontend to match the backend
(default), **new endpoint** = one of the known gaps (a) to (e), **remove** =
mock-only, dropped, **part 2** = a write action, compared here and wired in
part 2.

## Reads

| Function | Endpoint | Differences | Decision |
| -------- | -------- | ----------- | -------- |
| `listSims` | `GET /sims` | Query: mock `q`, `status`, `tier`, `department`, `carrierId`; backend `search` (phone or ICCID only, not employee name), `status`, `carrierId`, `operator`, `planType`, no `tier` or `department`. Items: mock adds `dataUsedGb`, `voiceUsed`, `voiceLimit`, `smsUsed`, `smsLimit`, `riskScore`; backend has none of them. Mock `tier` is `"Professional"`, backend `"professional"` or null. Backend `monthlyCost` is 0 for imported SIMs. | **frontend**: send `search`; drop the tier and department filters; replace the per-SIM data bar with the tier bundle and monthly cost from the new `allowance` field. **new endpoint (c)**: `allowance` on every SIM response and `monthlyCost` from the tier when the stored cost is 0. |
| `getSim` | `GET /sims/{id}` | Same shape as a list item. Profile looked up the carrier name from the mock store; backend sends `operator`. | **frontend**: use `operator`, lower-case tier labels. |
| `isPossibleSimId` | none (mock id range) | Server guard for a real 404. | **frontend**: the page asks `GET /sims/{id}` with the session token and calls `notFound()` on 404; the proxy only rejects ids that are not positive integers. |
| `getSimUsage` | `GET /sims/{id}/usage` | Backend returns raw days in MB, no billing cycle, no plan limits. Mock returned `{ cycleLength, cycleDay, series[{day,gb}], data, voice, sms }`. | **new endpoint (a)**: `GET /sims/{id}/usage/cycle`, cumulative data, voice and SMS against the tier allowance for the 30-day cycle that holds the as-of date. |
| `listSimAlerts` | none; `GET /alerts?simId=` | No `/sims/{id}/alerts`. Items are alert list items (same fields the table uses). | **frontend**: call `GET /alerts?simId=`. |
| `listSimAllocations` | `GET /sims/{id}/allocations` | Mock items `{ assignedAt, returnedAt, departmentName }`; backend `AllocationResponse` `{ assignmentDate, returnDate, isActive }` without department. | **frontend** field names; **new endpoint (d)**: `departmentName` on allocation responses. |
| `listSimActivity` | `GET /sims/{id}/activity` | Mock `{ id, at, kind, detail }` with kinds `created`, `allocated`, `returned`, `alert`, `tier`; backend `{ kind, occurredAt, summary, actorEmail, action, referenceType, referenceId }` with kinds `alertOpened`, `alertCleared`, `allocationStarted`, `allocationEnded`, `audit`; no id. | **frontend**: map the backend kinds to copy, key rows by `referenceType`, `referenceId` and `kind`. |
| `getSimRisk` | `GET /sims/{id}/risk?days=` | Same shape. Backend history ends today (UTC), so the 2025 demo scores fall outside a 90-day window. | **new endpoint (e)**, additive: optional `to` date; the alert screen passes the alert's `lastDetectedOn`. |
| `listAlerts` | `GET /alerts` | Same query and items. Backend adds `detectedOn`, `lastDetectedOn`; type `other` exists in the backend only. `createdAt` is the time the alert was stored (the backfill day). | **frontend**: show `detectedOn` (fallback `createdAt`) as "Detected", add the `other` type. |
| `getAlert` | `GET /alerts/{id}` | Same shape plus `detectedOn`, `lastDetectedOn`. | **frontend**: show detection dates. |
| `isPossibleAlertId` | none (mock ids) | Server guard for a real 404. | **frontend**: same as `isPossibleSimId`, with `GET /alerts/{id}`. |
| `getDashboard` | `GET /dashboard/summary?days=` | Mock `{ kpis{...}, alertsByType[{type,count}], dataWaste{paidGb,...}, tierChanges{upgrade,downgrade,keep}, needsReview[{phoneNumber,...}] }`; backend flat KPIs, `alertsByType` object, `needsReview` items with `simPhoneNumber`, `tierChanges: []`, `dataWaste: null`. Window ends now. | **frontend** maps the shape; **new endpoint (e)**: `asOf`, window on the as-of date and detection date; `dataWaste` filled from usage and the allowance table; `tierChanges` filled from open tier recommendations. |
| `getUsageAnalytics` | none | Fleet analytics did not exist. Mock filters `department` (name) and `plan`. | **new endpoint (b)**: `GET /usage/analytics?days=&departmentId=&tier=`. The plan filter becomes a tier filter (imported SIMs use the tier as plan type). |
| `getSampleDashboard` | none | Landing page preview built from the mock world. | **remove**: fixed sample numbers in `components/landing/landing-data.js`. |
| `referenceData` | mixed | Static departments, tiers, plan types, statuses, alert types, periods. | **frontend**: departments from `GET /departments` (`useDepartments`), tiers from the new `GET /tiers` (`useTiers`, gap (c)); statuses, alert types and periods stay as static vocabulary that mirrors the backend constants; `planTypes` was removed in part 2 (the SIM form picks a tier). |
| `listEmployees` | `GET /employees` | Same query (`departmentId`, `search`, paging). Backend adds `email`, `jobRole`, `createdAt`; orders by id, mock by name. | **frontend**: call it as is; the form sorts by name. |
| `listAllocations` | `GET /allocations` | Mock `q`, `status` (`active`/`ended`), `department`; backend `simId`, `employeeId`, `active` (bool). Backend orders by id ascending; mock newest first. No department on items. | **frontend**: `status` becomes `active=true/false`; drop `q` and the department filter; accept id order. **new endpoint (d)**: `departmentName`. |
| `listCarriers` | `GET /carriers` | Same, plain array with `simCount`. Mock adds `status` ("connected"). | **frontend**: remove the status badge. |
| `listAuditLogs` | `GET /audit-logs` | Same query. Backend `actorEmail` is an exact match (mock matched part of an address); items add `category`, `actorRole`. Admin gets only `system` rows. | **frontend**: search matches the whole email (placeholder says so); role-specific note. |
| (new) `listDepartments` | `GET /departments` | Paged `{ id, name, employeeCount }`. | **frontend**: new function for filters and forms. |
| (new) `listTiers` | `GET /tiers` | Tier allowance table. | **new endpoint (c)**. |

## Writes (part 2)

All writes call the backend. Each one bumps the data version on success and
on 409, so loaded screens reload. Errors reach users only through
`errorMessage()` or per-field lines from `copy.js`, never the backend
sentence. Buttons for actions the role cannot do are disabled with a reason.

| Function | Endpoint | Frontend handling |
| -------- | -------- | ----------------- |
| `createSim` | `POST /sims` | The form picks a tier (imports use the tier as plan type), then calls `setSimTier`. 400 maps each field to a form line; 409 now carries `errors.phoneNumber` or `errors.iccid` (backend addition) and marks that field. |
| `updateSim` | `PUT /sims/{id}` | Same as `createSim`; the plan type shows read only and the tier changes through `setSimTier`. |
| `setSimTier` | `PATCH /sims/{id}/tier` | Lower-case tiers; the screen reloads the SIM. |
| `setAlertStatus` | `PATCH /alerts/{id}/status` | Sends `resolution` and the trimmed note only when clearing. 409 shows a "status changed meanwhile" line. |
| `createAllocation` | `POST /allocations` | No date field: the backend uses the request time. "Handed over by" is an employee search that fills `assignedBy`. 409 marks the SIM as unavailable. |
| `endAllocation` | `POST /allocations/{id}/return` | 409 shows "already ended". |
| `addCarrier` | `POST /carriers` | 409 on `errors.name` shows "name taken". |
| `renameCarrier` | `PUT /carriers/{id}` | Same as `addCarrier`. |
| `removeCarrier` | `DELETE /carriers/{id}` | 409 with `simCount` opens the "move its SIMs first" dialog. |
| (new) `listTierRecommendations` | `GET /tier-recommendations` | Tier recommendations screen, filtered by `open`, `applied` or `dismissed`. |
| (new) `getTierRecommendationStatus` | `GET /tier-recommendations/status` | Last run, next allowed run date; the screen polls while `running`. |
| (new) `runTierRecommendations` | `POST /tier-recommendations/run` | 409 shows the `nextAllowedRunDate` extension; the screen never compares dates with the browser clock. |
| (new) `applyTierRecommendation` | `POST /tier-recommendations/{id}/apply` | Backend addition: sets the SIM tier and marks the recommendation `applied` in one transaction (migration `AddTierRecommendationApplied`). 409 when it is no longer open. |
| (new) `dismissTierRecommendation` | `POST /tier-recommendations/{id}/dismiss` | 409 when it is no longer open. |
| (new) `getScoringStatus` | `GET /admin/scoring/status` | Dashboard "Refresh scores" (Security Officer and Auditor): latest scored day and whether a run is queued or running; read every 3 s while one is, else every 60 s. |
| (new) `runScoring` | `POST /admin/scoring/run` | Security Officer only; a `merged` answer says the request joined the queued run. |
| (new) `getSystemStatus` | `GET /admin/system-status` | Admin System health: tiles and three charts; read every 30 s, paused while the tab is hidden. |
| (new) `listAccessRequests`, `reviewAccessRequest` | `GET /access-requests`, `PATCH /access-requests/{id}/status` | Admin, Requests tab. Approving creates the account and the API emails a generated password; the page never sees it. `accountCreated` false means the email already had an account; `emailSent` false opens the "use Resend sign-in email" warning. See `backend/docs/access-requests.md`. |
| (new) `listUsers`, `inviteUser` | `GET /users`, `POST /users` | Admin, Users tab. Invite sends name, email and role only; no password field exists in the UI. `emailSent` false shows the "try Resend" warning. |
| (new) `changeUserRole`, `disableUser`, `enableUser`, `unlockUser`, `resetUserMfa` | `PUT /users/{id}/role`, `POST /users/{id}/disable`, `/enable`, `/unlock`, `/mfa/reset` | Row actions. 409 on role change and disable means your own account or the last active Admin. |
| (new) `resendSignInEmail` | `POST /users/{id}/signin-email` | New generated password by email, also the way to recover a forgotten password. 409 for a disabled account. |

The mock data layer (`src/lib/mock`) is deleted.

## Summary

- 29 exports. 6 reads matched or needed only a call change (`getSim`,
  `listSimAlerts`, `listAlerts`, `getAlert`, `listEmployees`,
  `listCarriers`), 8 were adjusted on the frontend (`listSims`,
  `listSimAllocations`, `listSimActivity`, `listAllocations`,
  `listAuditLogs`, `isPossibleSimId`, `isPossibleAlertId`, `referenceData`),
  4 needed backend gaps (`getSimUsage` (a), `getUsageAnalytics` (b),
  `getDashboard` (e, plus data waste and tier changes), `getSimRisk` (`to`)),
  1 was removed (`getSampleDashboard`). The 10 writes were wired in part 2.
- Two read functions were added in part 1 (`listDepartments`, `listTiers`)
  and seven in part 2 (tier recommendations and Admin scoring).
- Backend additions: (a) `GET /sims/{id}/usage/cycle`, (b) `GET
  /usage/analytics`, (c) the `tier_allowances` table with `GET /tiers` and
  `allowance` on SIM responses, (d) `departmentName` on allocation
  responses, (e) `asOf` on dashboard and usage responses. See
  `backend/docs/api-carriers-usage.md` and `api-alerts-risk-audit.md`.
