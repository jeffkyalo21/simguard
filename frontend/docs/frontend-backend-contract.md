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
- The mock returned field error codes (`required`, `taken`); the backend
  returns sentences. Screens that showed codes must show `errorMessage()` or
  the backend sentence instead (part 2, write forms).
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
| `referenceData` | mixed | Static departments, tiers, plan types, statuses, alert types, periods. | **frontend**: departments from `GET /departments` (`useDepartments`), tiers from the new `GET /tiers` (`useTiers`, gap (c)); statuses, alert types and periods stay as static vocabulary that mirrors the backend constants; `planTypes` stays for the SIM form until part 2. |
| `listEmployees` | `GET /employees` | Same query (`departmentId`, `search`, paging). Backend adds `email`, `jobRole`, `createdAt`; orders by id, mock by name. | **frontend**: call it as is; the form sorts by name. |
| `listAllocations` | `GET /allocations` | Mock `q`, `status` (`active`/`ended`), `department`; backend `simId`, `employeeId`, `active` (bool). Backend orders by id ascending; mock newest first. No department on items. | **frontend**: `status` becomes `active=true/false`; drop `q` and the department filter; accept id order. **new endpoint (d)**: `departmentName`. |
| `listCarriers` | `GET /carriers` | Same, plain array with `simCount`. Mock adds `status` ("connected"). | **frontend**: remove the status badge. |
| `listAuditLogs` | `GET /audit-logs` | Same query. Backend `actorEmail` is an exact match (mock matched part of an address); items add `category`, `actorRole`. Admin gets only `system` rows. | **frontend**: search matches the whole email (placeholder says so); role-specific note. |
| (new) `listDepartments` | `GET /departments` | Paged `{ id, name, employeeCount }`. | **frontend**: new function for filters and forms. |
| (new) `listTiers` | `GET /tiers` | Tier allowance table. | **new endpoint (c)**. |

## Writes (part 2)

These keep their mock bodies in part 1. The comparison is for part 2.

| Function | Endpoint | Differences |
| -------- | -------- | ----------- |
| `createSim` | `POST /sims` | Body matches (`phoneNumber`, `iccid`, `carrierId`, `planType`, `monthlyCost`). Duplicates: backend 409 with a `detail` sentence, mock 409 with `errors.phoneNumber: ["taken"]`. 400 errors are sentences. |
| `updateSim` | `PUT /sims/{id}` | Same as `createSim`. |
| `setSimStatus` | `PATCH /sims/{id}/status` | Body `{ status }` matches; 409 when allocated. |
| `setSimTier` | `PATCH /sims/{id}/tier` | Backend takes lower-case tiers and returns `{ simId, tier, previousTier }`, not the SIM; it does not change cost or limits (the allowance follows the tier). |
| `setAlertStatus` | `PATCH /alerts/{id}/status` | Same body and transitions; 409 detail names the current status. |
| `createAllocation` | `POST /allocations` | `assignedBy` is an employee id and required; there is no `assignmentDate` (the backend uses the request time). |
| `endAllocation` | `POST /allocations/{id}/return` | Same; 409 when already returned. |
| `addCarrier` | `POST /carriers` | Same body; duplicate is 409 with `errors.name`. No `status`. |
| `renameCarrier` | `PUT /carriers/{id}` | Mock said PATCH; backend is PUT. |
| `removeCarrier` | `DELETE /carriers/{id}` | Backend answers 409 with `simCount` instead of `{ removed: false }`. |

## Summary

- 29 exports. 6 reads matched or needed only a call change (`getSim`,
  `listSimAlerts`, `listAlerts`, `getAlert`, `listEmployees`,
  `listCarriers`), 8 were adjusted on the frontend (`listSims`,
  `listSimAllocations`, `listSimActivity`, `listAllocations`,
  `listAuditLogs`, `isPossibleSimId`, `isPossibleAlertId`, `referenceData`),
  4 needed backend gaps (`getSimUsage` (a), `getUsageAnalytics` (b),
  `getDashboard` (e, plus data waste and tier changes), `getSimRisk` (`to`)),
  1 was removed (`getSampleDashboard`), 10 writes are left for part 2.
- Two functions were added: `listDepartments` and `listTiers`.
- Backend additions: (a) `GET /sims/{id}/usage/cycle`, (b) `GET
  /usage/analytics`, (c) the `tier_allowances` table with `GET /tiers` and
  `allowance` on SIM responses, (d) `departmentName` on allocation
  responses, (e) `asOf` on dashboard and usage responses. See
  `backend/docs/api-carriers-usage.md` and `api-alerts-risk-audit.md`.
