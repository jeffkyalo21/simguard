// Every screen reads and writes data only through these functions.
//
// Reads call the SimGuard API through src/lib/api.js (issue #14, step 5).
// The contract per function is in docs/frontend-backend-contract.md. Paged
// lists are { items, page, pageSize, total }; errors are ApiError.
//
// The demo data ends on 2025-03-31, so no read uses the browser clock as
// "now": relative ranges come from the as-of date (the latest usage date)
// that the dashboard and usage responses report.
//
// Writes call the API too and bump the data version after they succeed, so
// every screen on the page reloads what it shows (src/lib/data-version.js).
// There is no mock data: a screen shows the backend's answer or an error.

import { API_BASE, ApiError, api } from "@/lib/api";
import { bumpDataVersion } from "@/lib/data-version";
import { normalizePhone } from "@/lib/validation";

const MB_PER_GB = 1024;
const ALL = 100;

/**
 * Fixed vocabulary that mirrors the backend constants. Departments, tiers
 * and carriers change at run time and come from GET /departments,
 * GET /tiers and GET /carriers (src/hooks/use-reference.js).
 */
export const referenceData = {
  statuses: ["assigned", "available", "inactive"],
  alertTypes: ["inactiveBilling", "sharing", "reselling", "ghostUsage", "other"],
  periods: [7, 30, 90],
};

/** MB from the API as GB with one decimal, the unit the screens show. */
export function toGb(mb) {
  return Math.round((Number(mb) / MB_PER_GB) * 10) / 10;
}

/* --------------------------------------------------------- Reference data */

/** GET /departments, every department (up to 100), by id. */
export function listDepartments() {
  return api.get("/departments", { query: { pageSize: ALL } });
}

/** GET /tiers: the tier allowance table, smallest bundle first. */
export function listTiers() {
  return api.get("/tiers");
}

/** GET /carriers, oldest first, with the number of SIMs on each. */
export function listCarriers() {
  return api.get("/carriers");
}

/* ------------------------------------------------------------------ SIMs */

/**
 * GET /sims. `q` searches the phone number or ICCID (the API `search`).
 * @param {{ q?: string, status?: string, carrierId?: string | number,
 *   page?: number, pageSize?: number }} [query]
 */
export function listSims({ q, status, carrierId, page, pageSize } = {}) {
  return api.get("/sims", { query: { search: q?.trim(), status, carrierId, page, pageSize } });
}

/** GET /sims/{id}. Throws ApiError 404 when unknown. */
export function getSim(id) {
  return api.get(`/sims/${id}`);
}

/**
 * GET /sims/{id}/usage/cycle as the profile's cycle view: cumulative GB per
 * day of the 30-day cycle that holds the as-of date, and data, voice and
 * SMS used against the tier allowance (limits are null without a tier).
 */
export async function getSimUsage(id) {
  const cycle = await api.get(`/sims/${id}/usage/cycle`);
  const start = cycle.cycleStart ? Date.parse(`${cycle.cycleStart}T00:00:00Z`) : null;
  const dayOf = (date) => Math.round((Date.parse(`${date}T00:00:00Z`) - start) / 86_400_000) + 1;
  const allowance = cycle.allowance;

  return {
    asOf: cycle.asOf,
    tier: cycle.tier,
    cycleStart: cycle.cycleStart,
    cycleEnd: cycle.cycleEnd,
    cycleLength: cycle.cycleLength,
    cycleDay: cycle.daysElapsed,
    series: cycle.days.map((day) => ({ day: dayOf(day.date), date: day.date, gb: toGb(day.cumulativeDataMb) })),
    data: { used: toGb(cycle.used.dataMb), limit: allowance ? toGb(allowance.dataMb) : null },
    voice: { used: Math.round(cycle.used.voiceMinutes), limit: allowance?.voiceMinutes ?? null },
    sms: { used: cycle.used.sms, limit: allowance?.sms ?? null },
  };
}

/** GET /alerts?simId=: the SIM's alerts, newest first. */
export function listSimAlerts(id, { page, pageSize } = {}) {
  return api.get("/alerts", { query: { simId: id, page, pageSize } });
}

/** GET /sims/{id}/allocations. Items carry departmentName. */
export function listSimAllocations(id, { page, pageSize } = {}) {
  return api.get(`/sims/${id}/allocations`, { query: { page, pageSize } });
}

/**
 * GET /sims/{id}/activity, newest first. Items: { id, kind, occurredAt,
 * summary, actorEmail, action, referenceType, referenceId } where kind is
 * alertOpened, alertCleared, allocationStarted, allocationEnded or audit.
 * The API has no row id, so `id` is built from the reference and kind.
 */
export async function listSimActivity(id, { page, pageSize } = {}) {
  const result = await api.get(`/sims/${id}/activity`, { query: { page, pageSize } });
  return {
    ...result,
    items: result.items.map((item) => ({ ...item, id: `${item.referenceType}-${item.referenceId}-${item.kind}` })),
  };
}

/* ---------------------------------------------------------------- Alerts */

/**
 * GET /alerts. Items carry detectedOn and lastDetectedOn (the scored days)
 * besides createdAt (when the alert was stored).
 */
export function listAlerts({ status, type, simId, minScore, sort, order, page, pageSize } = {}) {
  return api.get("/alerts", { query: { status, type, simId, minScore, sort, order, page, pageSize } });
}

/**
 * How many alerts are open (flagged or escalated, not cleared), for the top
 * bar. Two single-row reads of GET /alerts; only the totals are used.
 * @returns {Promise<number>}
 */
export async function countOpenAlerts() {
  const [flagged, escalated] = await Promise.all([
    listAlerts({ status: "flagged", pageSize: 1 }),
    listAlerts({ status: "escalated", pageSize: 1 }),
  ]);
  return flagged.total + escalated.total;
}

/** GET /alerts/{id}, with the SIM, current allocation and recent alerts. */
export function getAlert(id) {
  return api.get(`/alerts/${id}`);
}

/**
 * GET /sims/{id}/risk. `to` ends the history on that date instead of today;
 * screens pass the alert's last detection day or the as-of date.
 */
export function getSimRisk(simId, { days = 90, to } = {}) {
  return api.get(`/sims/${simId}/risk`, { query: { days, to } });
}

/* ------------------------------------------------------------- Dashboard */

/**
 * GET /dashboard/summary for 7, 30 or 90 days up to the as-of date, in the
 * shape the dashboard draws.
 */
export async function getDashboard(days = 30) {
  const summary = await api.get("/dashboard/summary", { query: { days } });
  const waste = summary.dataWaste;
  const count = (recommendation) =>
    summary.tierChanges.find((item) => item.recommendation === recommendation)?.count ?? 0;

  return {
    days: summary.days,
    asOf: summary.asOf,
    kpis: {
      activeSims: summary.activeSims,
      flagged: summary.flagged,
      toReview: summary.toReview,
      resolved: summary.resolved,
    },
    dataWaste: waste
      ? {
          paidGb: toGb(waste.paidMb),
          usedGb: toGb(waste.usedMb),
          unusedGb: toGb(waste.unusedMb),
          percent: waste.unusedPercent,
          departments: waste.departments.map((item) => ({ name: item.name, unusedGb: toGb(item.unusedMb) })),
        }
      : { paidGb: 0, usedGb: 0, unusedGb: 0, percent: 0, departments: [] },
    alertsByType: referenceData.alertTypes.map((type) => ({ type, count: summary.alertsByType[type] ?? 0 })),
    needsReview: summary.needsReview,
    tierChanges: { upgrade: count("upgrade"), downgrade: count("downgrade") },
  };
}

/* ----------------------------------------------------------------- Usage */

function usageRow(row) {
  return {
    id: row.simId,
    phoneNumber: row.phoneNumber,
    employeeName: row.employeeName,
    departmentName: row.departmentName,
    tier: row.tier,
    carrier: row.carrier,
    dataUsedGb: toGb(row.dataUsedMb),
    dataPaidGb: toGb(row.dataPaidMb),
    utilisationPercent: row.utilisationPercent,
  };
}

function usageGroup(group) {
  return { name: group.name, simCount: group.simCount, dataUsedGb: toGb(group.dataUsedMb), dataPaidGb: toGb(group.dataPaidMb) };
}

/**
 * GET /usage/analytics for 7, 30 or 90 days up to the as-of date, in GB.
 * @param {{ days?: number, departmentId?: number | string, tier?: string }} [query]
 */
export async function getUsageAnalytics({ days = 30, departmentId, tier } = {}) {
  const result = await api.get("/usage/analytics", { query: { days, departmentId, tier } });
  return {
    days: result.days,
    asOf: result.asOf,
    from: result.from,
    to: result.to,
    totals: {
      simCount: result.totals.simCount,
      dataUsedGb: toGb(result.totals.dataUsedMb),
      dataPaidGb: toGb(result.totals.dataPaidMb),
      unusedGb: toGb(result.totals.unusedMb),
      utilisationPercent: result.totals.utilisationPercent,
    },
    daily: result.daily.map((point) => ({ date: point.date, dataGb: toGb(point.dataMb) })),
    byDepartment: result.byDepartment.map(usageGroup),
    byTier: result.byTier.map(usageGroup),
    topSims: result.topSims.map(usageRow),
    leastUsedSims: result.leastUsedSims.map(usageRow),
  };
}

/* ----------------------------------------------------------- Allocations */

/**
 * GET /employees. Items: { id, name, email, departmentId, departmentName,
 * jobRole, createdAt }, by id.
 * @param {{ departmentId?: number | string, search?: string, page?: number,
 *   pageSize?: number }} [query]
 */
export function listEmployees({ departmentId, search, page, pageSize } = {}) {
  return api.get("/employees", { query: { departmentId, search: search?.trim(), page, pageSize } });
}

/**
 * GET /allocations, by id. `status` is "active" or "ended" (the API's
 * active=true or false). Items carry departmentName.
 * @param {{ status?: string, page?: number, pageSize?: number }} [query]
 */
export function listAllocations({ status, page, pageSize } = {}) {
  const active = status === "active" ? true : status === "ended" ? false : undefined;
  return api.get("/allocations", { query: { active, page, pageSize } });
}

/* ------------------------------------------------------------- Audit log */

/**
 * GET /audit-logs, newest first. Auditor sees every entry, Admin only the
 * system entries (the API filters them). `actorEmail` is an exact match
 * (ignoring case); `from` and `to` are UTC timestamps, inclusive.
 * @param {{ simId?: number | string, actorEmail?: string, action?: string,
 *   from?: string, to?: string, page?: number, pageSize?: number }} [query]
 */
export function listAuditLogs({ simId, actorEmail, action, from, to, page, pageSize } = {}) {
  return api.get("/audit-logs", { query: { simId, actorEmail: actorEmail?.trim(), action, from, to, page, pageSize } });
}

/* ================================================================ Writes */

/**
 * Runs a write and, once it succeeds, tells every screen to reload. A 409
 * means the screen showed stale data (someone else changed it first), so
 * the screens reload then too before the error reaches the caller.
 */
async function write(call) {
  try {
    const result = await call;
    bumpDataVersion();
    return result;
  } catch (error) {
    if (error instanceof ApiError && error.status === 409) bumpDataVersion();
    throw error;
  }
}

/** The SimRequest body: E.164 phone, trimmed ICCID, cost in KES. */
function simBody({ phoneNumber, iccid, carrierId, planType, monthlyCost }) {
  return {
    phoneNumber: normalizePhone(phoneNumber) ?? String(phoneNumber ?? "").trim(),
    iccid: String(iccid ?? "").trim(),
    carrierId: Number(carrierId),
    planType: String(planType ?? "").trim(),
    monthlyCost: Math.round(Number(monthlyCost) * 100) / 100,
  };
}

/**
 * POST /sims. New SIMs start as "available". A duplicate phone number or
 * ICCID is a 409 with errors.phoneNumber or errors.iccid.
 */
export function createSim(values) {
  return write(api.post("/sims", simBody(values)));
}

/** PUT /sims/{id}, same body and errors as createSim. */
export function updateSim(id, values) {
  return write(api.put(`/sims/${id}`, simBody(values)));
}

/** PATCH /sims/{id}/status with "available" or "inactive"; 409 while allocated. */
export function setSimStatus(id, status) {
  return write(api.patch(`/sims/${id}/status`, { status }));
}

/**
 * PATCH /sims/{id}/tier with a lower-case tier. Returns { simId, tier,
 * previousTier }; the allowance and cost follow the tier.
 */
export function setSimTier(id, tier) {
  return write(api.patch(`/sims/${id}/tier`, { tier }));
}

/**
 * PATCH /alerts/{id}/status. Clearing needs a resolution (confirmedMisuse or
 * falsePositive) and takes an optional note; 409 when the move is not
 * allowed from the alert's current status.
 */
export function setAlertStatus(id, { status, resolution, resolutionNote } = {}) {
  const closing = status === "cleared";
  const note = resolutionNote?.trim();
  return write(
    api.patch(`/alerts/${id}/status`, {
      status,
      ...(closing && { resolution }),
      ...(closing && note && { resolutionNote: note }),
    })
  );
}

/**
 * POST /allocations. `assignedBy` is the employee who hands the SIM over.
 * The backend dates the allocation at the time of the request. 409 when
 * the SIM is not available or already allocated.
 * @param {{ simId: number | string, employeeId: number | string,
 *   assignedBy: number | string }} body
 */
export function createAllocation({ simId, employeeId, assignedBy }) {
  return write(
    api.post("/allocations", { simId: Number(simId), employeeId: Number(employeeId), assignedBy: Number(assignedBy) })
  );
}

/** POST /allocations/{id}/return. 409 when already returned. */
export function endAllocation(id) {
  return write(api.post(`/allocations/${id}/return`));
}

/* -------------------------------------------------------------- Carriers */

/** POST /carriers. A duplicate name is a 409 with errors.name. */
export function addCarrier(name) {
  return write(api.post("/carriers", { name: String(name ?? "").trim() }));
}

/** PUT /carriers/{id} with a new name; errors as addCarrier. */
export function renameCarrier(id, name) {
  return write(api.put(`/carriers/${id}`, { name: String(name ?? "").trim() }));
}

/**
 * DELETE /carriers/{id}. A carrier that SIMs still use is a 409 whose
 * extensions.simCount says how many.
 */
export function removeCarrier(id) {
  return write(api.delete(`/carriers/${id}`));
}

/* ------------------------------------------------------- Carrier usage feed */

/**
 * PUT /carriers/{id}/feed. `apiKey` is optional: leave it out to keep the
 * saved key. The key is never returned, only `feedKeySet`.
 * @param {number} id
 * @param {{ baseUrl: string, enabled: boolean, startDate?: string, apiKey?: string }} settings
 */
export function saveCarrierFeed(id, { baseUrl, enabled, startDate, apiKey }) {
  const body = { baseUrl: String(baseUrl ?? "").trim(), enabled: Boolean(enabled) };
  if (startDate) body.startDate = startDate;
  if (apiKey) body.apiKey = apiKey;
  return write(api.put(`/carriers/${id}/feed`, body));
}

/** PUT /auth/me. Changes the signed-in user's own name; returns the profile. */
export function updateProfileName(name) {
  return api.put("/auth/me", { name: String(name ?? "").trim() });
}

/**
 * POST /auth/mfa/recovery-codes. Makes 10 new recovery codes (the old ones
 * stop working) after the password and a current authenticator code.
 * @returns {Promise<{ recoveryCodes: string[] }>}
 */
export function regenerateRecoveryCodes({ password, code }) {
  return api.post("/auth/mfa/recovery-codes", { password, code: String(code ?? "").trim() });
}

/** POST /auth/me/delete. Deletes the signed-in account (204). */
export function deleteMyAccount({ password, code }) {
  return api.post("/auth/me/delete", { password, code: String(code ?? "").trim() });
}

/**
 * POST /carriers/{id}/feed/test. Always 200 when the call was made:
 * `{ ok, message, date }` says how the carrier answered. Typed values are
 * tried without saving them; a missing one comes from the saved settings.
 */
export function testCarrierFeed(id, { baseUrl, apiKey } = {}) {
  const body = {};
  if (baseUrl) body.baseUrl = String(baseUrl).trim();
  if (apiKey) body.apiKey = apiKey;
  return api.post(`/carriers/${id}/feed/test`, body);
}

/** POST /carriers/{id}/feed/sync: starts a run (202); 409 when one is running. */
export function syncCarrierFeed(id) {
  return write(api.post(`/carriers/${id}/feed/sync`));
}

/* ---------------------------------------------------- Tier recommendations */

/**
 * GET /tier-recommendations, newest first.
 * @param {{ status?: "open" | "dismissed" | "applied", page?: number,
 *   pageSize?: number }} [query]
 */
export function listTierRecommendations({ status, page, pageSize } = {}) {
  return api.get("/tier-recommendations", { query: { status, page, pageSize } });
}

/** GET /tier-recommendations/status: last run, interval and whether one runs. */
export function getTierRecommendationStatus() {
  return api.get("/tier-recommendations/status");
}

/**
 * POST /tier-recommendations/run. 202 with { runId }; 409 while a run is in
 * progress or before the interval has passed (extensions.nextAllowedRunDate).
 */
export function runTierRecommendations() {
  return write(api.post("/tier-recommendations/run"));
}

/** POST /tier-recommendations/{id}/apply: sets the SIM to the recommended tier. */
export function applyTierRecommendation(id) {
  return write(api.post(`/tier-recommendations/${id}/apply`));
}

/** POST /tier-recommendations/{id}/dismiss. 409 when no longer open. */
export function dismissTierRecommendation(id) {
  return write(api.post(`/tier-recommendations/${id}/dismiss`));
}

/* ------------------------------------------------------- Scoring (Admin) */

/**
 * GET /admin/scoring/status: schedule, ML service health, totals and the
 * last runs. Totals only, never SIM-level data. Security Officer and Auditor.
 */
export function getScoringStatus() {
  return api.get("/admin/scoring/status");
}

/**
 * GET /admin/system-status: health of the API, database, ML service, email
 * and scoring, user and request counts, and 14 days of sign-ins and scoring
 * runs. No organisation data and no secrets. Admin only.
 */
export function getSystemStatus() {
  return api.get("/admin/system-status");
}

/**
 * POST /admin/scoring/run. 202 with { runId, merged }; merged is true when
 * a queued run already covers the request.
 */
export function runScoring() {
  return write(api.post("/admin/scoring/run"));
}

/* ------------------------------------------------- Access requests (Admin) */

/**
 * GET /access-requests, newest first. `status` is pending, approved or
 * declined. Admin only.
 * @param {{ status?: string, page?: number, pageSize?: number }} [query]
 */
export function listAccessRequests({ status, page, pageSize } = {}) {
  return api.get("/access-requests", { query: { status, page, pageSize } });
}

/**
 * How many access requests wait for a decision, for the top bar. Admin only.
 * @returns {Promise<number>}
 */
export async function countPendingAccessRequests() {
  const page = await listAccessRequests({ status: "pending", pageSize: 1 });
  return page.total;
}

/**
 * PATCH /access-requests/{id}/status to approved or declined. 409 when
 * already reviewed. Approving creates the account and the API emails a
 * generated password; the browser never sees it. The reply has `emailSent`
 * (true or false when an email was due, null when not) and, for approved,
 * `accountCreated` (false when an account with that email already existed).
 */
export function reviewAccessRequest(id, status) {
  return write(api.patch(`/access-requests/${id}/status`, { status }));
}

/* ------------------------------------------------------- Users (Admin) */

/** GET /users, by id. Admin only. */
export function listUsers({ page, pageSize } = {}) {
  return api.get("/users", { query: { page, pageSize } });
}

/**
 * POST /users: invite a person. The API generates a password and emails it;
 * the reply has `emailSent`. 409 when the email is taken.
 * @param {{ name: string, email: string, role: string }} body
 */
export function inviteUser({ name, email, role }) {
  return write(api.post("/users", { name, email, role }));
}

/** PUT /users/{id}/role. 409 for your own account or the last active Admin. */
export function changeUserRole(id, role) {
  return write(api.put(`/users/${id}/role`, { role }));
}

/** POST /users/{id}/disable. 409 for your own account or the last active Admin. */
export function disableUser(id) {
  return write(api.post(`/users/${id}/disable`));
}

/** POST /users/{id}/enable. */
export function enableUser(id) {
  return write(api.post(`/users/${id}/enable`));
}

/** POST /users/{id}/unlock: clears a lockout, the password stays. */
export function unlockUser(id) {
  return write(api.post(`/users/${id}/unlock`));
}

/** POST /users/{id}/mfa/reset: the person enrols again at the next sign-in. */
export function resetUserMfa(id) {
  return write(api.post(`/users/${id}/mfa/reset`));
}

/**
 * POST /users/{id}/signin-email: new generated password, emailed to the
 * person. The reply has `emailSent`. 409 when the account is disabled.
 */
export function resendSignInEmail(id) {
  return write(api.post(`/users/${id}/signin-email`));
}

/* --------------------------------------------------------------- Imports */

/** CSV import types in the order they should be imported. */
export const IMPORT_TYPES = ["departments", "employees", "sims", "allocations", "usage"];

/** The file size the API accepts, in bytes. */
export const IMPORT_MAX_BYTES = 25 * 1024 * 1024;

/** GET /imports/{type}/template: a plain link, the browser downloads the CSV. */
export function importTemplateUrl(type) {
  return `${API_BASE}/imports/${type}/template`;
}

function importForm(file) {
  const form = new FormData();
  form.append("file", file, file.name);
  return form;
}

/** POST /imports/{type}?mode=preview: validates the file, saves nothing. */
export function previewImport(type, file, { signal } = {}) {
  return api.post(`/imports/${type}`, importForm(file), { query: { mode: "preview" }, signal });
}

/**
 * POST /imports/{type}?mode=commit. A 422 (row errors without
 * skipInvalid) comes back as a normal result with status "rejected", shown
 * like preview errors; every other failure throws an ApiError.
 */
export async function commitImport(type, file, { skipInvalid = false } = {}) {
  try {
    return await write(
      api.post(`/imports/${type}`, importForm(file), { query: { mode: "commit", skipInvalid: skipInvalid || undefined } })
    );
  } catch (error) {
    if (error instanceof ApiError && error.status === 422 && error.body?.status === "rejected") {
      // The rejected run is in the history now.
      bumpDataVersion();
      return error.body;
    }
    throw error;
  }
}

/** GET /imports: past commit attempts, newest first. */
export function listImportRuns({ page, pageSize } = {}) {
  return api.get("/imports", { query: { page, pageSize } });
}

/* ------------------------------------------------------- First-day setup */

/**
 * What the first-day checklist needs: how much of each kind of data exists.
 * Totals come from list endpoints with pageSize 1; usage is done once the
 * dashboard has an as-of date (the latest usage day).
 */
export async function getSetupProgress() {
  const [carriers, departments, employees, sims, allocations, summary, scores] = await Promise.all([
    listCarriers(),
    api.get("/departments", { query: { pageSize: 1 } }),
    api.get("/employees", { query: { pageSize: 1 } }),
    api.get("/sims", { query: { pageSize: 1 } }),
    api.get("/allocations", { query: { pageSize: 1 } }),
    api.get("/dashboard/summary", { query: { days: 7 } }),
    api.get("/risk-scores", { query: { pageSize: 1 } }),
  ]);

  return {
    carriers: carriers.length,
    departments: departments.total,
    employees: employees.total,
    sims: sims.total,
    allocations: allocations.total,
    usageAsOf: summary.asOf ?? null,
    scores: scores.total,
  };
}
