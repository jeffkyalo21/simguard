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
// TEMPORARY (part 2): the write functions below the reads still change the
// mock database in src/lib/mock. Part 2 replaces each body with its API
// call (see "Writes (part 2)" in the contract).

import { ApiError, api } from "@/lib/api";
import { MOCK_USER, PLAN_TYPES, TIERS, fixtures } from "@/lib/mock/fixtures";
import { dayToUtc, todayDay } from "@/lib/format";
import { readMockDb, writeMockDb } from "@/lib/mock/db";
import { carrierNameError, normalizePhone, simFieldErrors } from "@/lib/validation";

const MB_PER_GB = 1024;
const ALL = 100;

/**
 * Fixed vocabulary that mirrors the backend constants. Departments, tiers
 * and carriers change at run time and come from GET /departments,
 * GET /tiers and GET /carriers (src/hooks/use-reference.js).
 */
export const referenceData = {
  /** For the SIM form until part 2 wires its write. */
  planTypes: PLAN_TYPES,
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

/* ================================================== Writes (mock, part 2) */

const DELAY_MS = 300;

function wait(ms = DELAY_MS) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

const notFound = () => new ApiError({ status: 404 });

function findSim(id) {
  return readMockDb().sims.find((sim) => sim.id === Number(id));
}

/** Mock tiers are capitalised ("Support"); the API's are lower case. */
function mockTier(tier) {
  const name = String(tier ?? "");
  return name.charAt(0).toUpperCase() + name.slice(1);
}

/** PATCH /alerts/{id}/status with contract transition and validation rules. */
export async function setAlertStatus(id, { status, resolution, resolutionNote } = {}) {
  await wait();
  const alert = readMockDb().alerts.find((item) => item.id === Number(id));
  if (!alert) throw notFound();
  if (!["flagged", "escalated", "cleared"].includes(status)) throw fieldError("status", "invalid");
  const closing = status === "cleared";
  if (closing && !["confirmedMisuse", "falsePositive"].includes(resolution)) throw fieldError("resolution", resolution ? "invalid" : "required");
  if (!closing && (resolution !== undefined || resolutionNote !== undefined)) throw fieldError(resolution !== undefined ? "resolution" : "resolutionNote", "invalid");
  if (resolutionNote !== undefined && (typeof resolutionNote !== "string" || resolutionNote.length > 500)) throw fieldError("resolutionNote", "tooLong");

  const allowed = (alert.status === "flagged" || alert.status === "escalated") && closing
    || alert.status === "cleared" && status === "escalated"
    || alert.status === "escalated" && status === "flagged";
  if (!allowed) throw new ApiError({ status: 409, body: { currentStatus: alert.status } });

  const at = new Date().toISOString();
  const updated = {
    ...alert,
    status,
    resolvedAt: closing ? at : null,
    resolution: closing ? resolution : null,
    resolutionNote: closing ? (resolutionNote?.trim() || null) : null,
    resolvedBy: closing ? { id: MOCK_USER.id, name: MOCK_USER.name, email: MOCK_USER.email } : null,
  };
  writeMockDb({
    type: "alert/changed",
    alert: updated,
    activity: { id: `alert-${alert.id}-${at}`, simId: alert.simId, at, kind: "alertStatus", detail: { type: alert.type, status, resolution: updated.resolution } },
    audit: auditRow("alert.status_changed", findSim(alert.simId), alertSnapshot(alert), alertSnapshot(updated), at),
  });
  return getAlert(id);
}

function fieldError(field, code) {
  return new ApiError({ status: 400, errors: { [field]: [code] } });
}

/**
 * Same rules as the API's SimRequest validation. ApiError.errors maps each
 * field to error codes (required, invalid, tooLong, taken); the form maps
 * codes to copy.
 */
function validateSim(body, excludeId) {
  const fieldErrors = simFieldErrors(body);

  if (Object.keys(fieldErrors).length) {
    throw new ApiError({
      status: 400,
      errors: Object.fromEntries(Object.entries(fieldErrors).map(([field, code]) => [field, [code]])),
    });
  }

  const phoneNumber = normalizePhone(body.phoneNumber);
  const iccid = body.iccid.trim();

  // The API answers 409 for duplicates; the mock names the field.
  const others = readMockDb().sims.filter((sim) => sim.id !== excludeId);
  if (others.some((sim) => sim.phoneNumber === phoneNumber)) {
    throw new ApiError({ status: 409, errors: { phoneNumber: ["taken"] } });
  }
  if (others.some((sim) => sim.iccid === iccid)) {
    throw new ApiError({ status: 409, errors: { iccid: ["taken"] } });
  }

  const carrierId = Number(body.carrierId);
  if (!readMockDb().carriers.some((carrier) => carrier.id === carrierId)) {
    throw fieldError("carrierId", "invalid");
  }

  return {
    phoneNumber,
    iccid,
    carrierId,
    planType: body.planType.trim(),
    monthlyCost: Math.round(Number(body.monthlyCost) * 100) / 100,
  };
}

/**
 * Saves a SIM, logs it in the SIM's activity and, when `audit` is given as
 * { action, oldValues, newValues }, in the audit log.
 */
function save(sim, kind, detail, audit) {
  const at = new Date().toISOString();
  writeMockDb({
    type: "sim/saved",
    sim,
    activity: { id: `e${sim.id}-${at}-${kind}`, simId: sim.id, at, kind, detail },
    audit: audit && auditRow(audit.action, sim, audit.oldValues, audit.newValues, at),
  });
  return sim;
}

/** POST /sims. New SIMs start as "available". */
export async function createSim(body) {
  await wait();
  const values = validateSim(body);
  const db = readMockDb();
  const tier = TIERS.Support;

  const sim = {
    id: Math.max(...db.sims.map((s) => s.id)) + 1,
    ...values,
    status: "available",
    createdAt: new Date().toISOString(),
    currentAllocation: null,
    // future field
    tier: "Support",
    dataUsedGb: 0,
    voiceUsed: 0,
    voiceLimit: tier.voiceMin,
    smsUsed: 0,
    smsLimit: tier.sms,
    riskScore: 0,
  };
  return save(sim, "created", undefined, { action: "sim.created", oldValues: null, newValues: simSnapshot(sim) });
}

/** PUT /sims/{id}. */
export async function updateSim(id, body) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();
  const updated = { ...sim, ...validateSim(body, sim.id) };
  return save(updated, "edited", undefined, { action: "sim.updated", oldValues: simSnapshot(sim), newValues: simSnapshot(updated) });
}

/** PATCH /sims/{id}/status with "available" or "inactive". */
export async function setSimStatus(id, status) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();
  if (!["available", "inactive"].includes(status)) throw fieldError("status", "invalid");
  // Same rule as the API: an allocated SIM must be returned first.
  if (sim.currentAllocation) throw new ApiError({ status: 409 });
  return save({ ...sim, status }, status, undefined, { action: "sim.status_changed", oldValues: { status: sim.status }, newValues: { status } });
}

// future field: tier changes have no endpoint yet.
/** Changes a SIM's tier and its bundle limits. */
export async function setSimTier(id, tier) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();
  const name = mockTier(tier);
  const limits = TIERS[name];
  if (!limits) throw fieldError("tier", "invalid");

  // future field: the API has no tier yet, so the audit row records it as a
  // SIM edit with the tier and the cost it sets.
  return save(
    { ...sim, tier: name, voiceLimit: limits.voiceMin, smsLimit: limits.sms, monthlyCost: limits.monthlyCost },
    "tier",
    tier,
    {
      action: "sim.updated",
      oldValues: { tier: sim.tier, monthlyCost: sim.monthlyCost },
      newValues: { tier: name, monthlyCost: limits.monthlyCost },
    }
  );
}


/** The AllocationResponse shape, plus the employee's department. */
function toAllocationResponse(allocation, db = readMockDb()) {
  const sim = db.sims.find((item) => item.id === allocation.simId);
  const assigner = db.employees.find((employee) => employee.id === allocation.assignedBy);
  return {
    id: allocation.id,
    simId: allocation.simId,
    simPhoneNumber: sim?.phoneNumber ?? "",
    employeeId: allocation.employeeId,
    employeeName: allocation.employeeName,
    assignedBy: allocation.assignedBy,
    assignedByName: assigner?.name ?? "",
    assignmentDate: allocation.assignedAt,
    returnDate: allocation.returnedAt,
    isActive: !allocation.returnedAt,
    // future field: the API's AllocationResponse has no department yet.
    departmentName: allocation.departmentName,
  };
}

function conflict(detail) {
  return new ApiError({ status: 409, title: "Conflict", detail });
}

/**
 * POST /allocations. Same rules as the API: ids are required (400 per
 * field), only an available SIM can be allocated and a SIM has one active
 * allocation at most (409). assignedBy defaults to the signed-in user.
 * future field: assignmentDate ("YYYY-MM-DD", not in the future); the API
 * always uses the time of the request.
 * @param {{ simId: number | string, employeeId: number | string,
 *   assignedBy?: number | string, assignmentDate?: string }} body
 */
export async function createAllocation({ simId, employeeId, assignedBy = fixtures.assignerId, assignmentDate } = {}) {
  await wait();
  const db = readMockDb();
  const errors = {};
  const ids = { simId: Number(simId), employeeId: Number(employeeId), assignedBy: Number(assignedBy) };

  for (const [field, value] of Object.entries(ids)) {
    if (!Number.isInteger(value) || value <= 0) errors[field] = ["required"];
  }

  const now = new Date();
  const today = dayToUtc(todayDay(now), "start");
  const start = assignmentDate ? dayToUtc(assignmentDate, "start") : null;
  if (assignmentDate !== undefined && !start) errors.assignmentDate = ["invalid"];
  else if (start && start > today) errors.assignmentDate = ["future"];

  const sim = db.sims.find((item) => item.id === ids.simId);
  const employee = db.employees.find((item) => item.id === ids.employeeId);
  if (!errors.simId && !sim) errors.simId = ["invalid"];
  if (!errors.employeeId && !employee) errors.employeeId = ["invalid"];
  if (!errors.assignedBy && !db.employees.some((item) => item.id === ids.assignedBy)) errors.assignedBy = ["invalid"];
  if (Object.keys(errors).length) throw new ApiError({ status: 400, errors });

  if (sim.status !== "available") {
    throw conflict(`SIM ${sim.id} is '${sim.status}' and cannot be assigned. Only available SIMs can be assigned.`);
  }
  if (db.allocations.some((item) => item.simId === sim.id && !item.returnedAt)) {
    throw conflict(`SIM ${sim.id} already has an active allocation.`);
  }

  // A start date of today uses the current time, like the API.
  const assignedAt = !start || start === today ? now.toISOString() : new Date(Date.parse(start) + 9 * 3_600_000).toISOString();
  const allocation = {
    id: Math.max(0, ...db.allocations.map((item) => item.id)) + 1,
    simId: sim.id,
    employeeId: employee.id,
    employeeName: employee.name,
    departmentName: employee.departmentName,
    assignedAt,
    returnedAt: null,
    assignedBy: ids.assignedBy,
  };
  const updatedSim = {
    ...sim,
    status: "assigned",
    currentAllocation: {
      allocationId: allocation.id,
      employeeId: employee.id,
      employeeName: employee.name,
      departmentName: employee.departmentName,
      assignedAt,
    },
  };

  writeMockDb({
    type: "allocation/saved",
    allocation,
    sim: updatedSim,
    audit: auditRow(
      "allocation.created",
      sim,
      { simStatus: sim.status },
      { allocationId: allocation.id, employeeId: employee.id, assignedBy: ids.assignedBy, assignmentDate: assignedAt, simStatus: "assigned" }
    ),
  });
  return toAllocationResponse(allocation);
}

/**
 * POST /allocations/{id}/return. 404 when unknown, 409 when already ended.
 * The SIM becomes available again unless it is inactive.
 */
export async function endAllocation(id) {
  await wait();
  const db = readMockDb();
  const allocation = db.allocations.find((item) => item.id === Number(id));
  if (!allocation) throw notFound();
  if (allocation.returnedAt) {
    throw conflict(`Allocation ${allocation.id} was already returned on ${allocation.returnedAt}.`);
  }

  const sim = db.sims.find((item) => item.id === allocation.simId);
  const returnedAt = new Date().toISOString();
  const ended = { ...allocation, returnedAt };
  const status = sim.status === "inactive" ? "inactive" : "available";
  const updatedSim = { ...sim, status, currentAllocation: null };

  writeMockDb({
    type: "allocation/saved",
    allocation: ended,
    sim: updatedSim,
    audit: auditRow(
      "allocation.returned",
      sim,
      { allocationId: allocation.id, returnDate: null, simStatus: sim.status },
      { allocationId: allocation.id, returnDate: returnedAt, simStatus: status }
    ),
  });
  return toAllocationResponse(ended);
}

/* -------------------------------------------------------------- Carriers */

// future endpoints: carriers are not in the backend yet. Names follow
// carrierNameError: required, trimmed, 2 to 40 characters, unique ignoring
// case. Errors are ApiError 400 with errors.name = [code]; a duplicate is
// a 409 with errors.name = ["taken"].

function carrierName(name, excludeId) {
  const code = carrierNameError(name, readMockDb().carriers, excludeId);
  if (code === "taken") {
    throw new ApiError({ status: 409, title: "Conflict", errors: { name: ["taken"] } });
  }
  if (code) throw fieldError("name", code);
  return name.trim();
}

function findCarrier(id) {
  return readMockDb().carriers.find((carrier) => carrier.id === Number(id));
}

/** Number of SIMs on a carrier. */
function simsUsing(id) {
  return readMockDb().sims.filter((sim) => sim.carrierId === Number(id)).length;
}


/** POST /carriers. New carriers start as "not connected". */
export async function addCarrier(name) {
  await wait();
  const carriers = readMockDb().carriers;
  const carrier = {
    id: Math.max(0, ...carriers.map((c) => c.id)) + 1,
    name: carrierName(name),
    status: "not connected",
    createdAt: new Date().toISOString(),
  };
  writeMockDb({ type: "carrier/saved", carrier });
  return carrier;
}

/** PATCH /carriers/{id} with a new name. */
export async function renameCarrier(id, name) {
  await wait();
  const carrier = findCarrier(id);
  if (!carrier) throw notFound();
  const renamed = { ...carrier, name: carrierName(name, carrier.id) };
  writeMockDb({ type: "carrier/saved", carrier: renamed });
  return renamed;
}

/**
 * DELETE /carriers/{id}. Blocked while any SIM uses the carrier.
 * @returns {Promise<{ removed: boolean, simCount: number }>} simCount is the
 *   number of SIMs on the carrier, so the UI can say why it was kept.
 */
export async function removeCarrier(id) {
  await wait();
  const carrier = findCarrier(id);
  if (!carrier) throw notFound();
  const simCount = simsUsing(carrier.id);
  if (simCount > 0) return { removed: false, simCount };
  writeMockDb({ type: "carrier/removed", id: carrier.id });
  return { removed: true, simCount: 0 };
}

/* ------------------------------------------------------------- Audit log */

/** The SIM fields the API records for sim.* actions. */
function simSnapshot(sim) {
  return {
    id: sim.id,
    phoneNumber: sim.phoneNumber,
    iccid: sim.iccid,
    operator: readMockDb().carriers.find((carrier) => carrier.id === sim.carrierId)?.name ?? null,
    planType: sim.planType,
    monthlyCost: sim.monthlyCost,
    status: sim.status,
  };
}

/** The alert fields the API records for alert.status_changed. */
function alertSnapshot(alert) {
  return {
    alertId: alert.id,
    status: alert.status,
    resolution: alert.resolution ?? null,
    resolutionNote: alert.resolutionNote ?? null,
    resolvedAt: alert.resolvedAt ?? null,
    resolvedByUserId: alert.resolvedBy?.id ?? null,
  };
}

/** One GET /audit-logs item written by the signed-in user. */
function auditRow(action, sim, oldValues, newValues, timestamp = new Date().toISOString()) {
  const rows = readMockDb().audit;
  return {
    id: Math.max(0, ...rows.map((row) => row.id)) + 1,
    timestamp,
    actorUserId: MOCK_USER.id,
    actorEmail: MOCK_USER.email,
    employeeId: null,
    action,
    affectedSimId: sim?.id ?? null,
    affectedSimPhoneNumber: sim?.phoneNumber ?? null,
    oldValues,
    newValues,
  };
}
