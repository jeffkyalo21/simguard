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

import { ApiError, api } from "@/lib/api";
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
 * last runs. Totals only, never SIM-level data. Admin only.
 */
export function getScoringStatus() {
  return api.get("/admin/scoring/status");
}

/**
 * POST /admin/scoring/run. 202 with { runId, merged }; merged is true when
 * a queued run already covers the request.
 */
export function runScoring() {
  return write(api.post("/admin/scoring/run"));
}
