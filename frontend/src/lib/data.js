// Every screen reads and writes data only through these functions.
//
// TEMPORARY (issue #17): the bodies return mock data after a short delay so
// loading states are visible. Issue #20 replaces each body with an `api`
// call from src/lib/api.js; inputs and outputs already follow the backend
// shapes (paged lists are { items, page, pageSize, total }, errors are
// ApiError). Fields marked "future field" do not exist in the backend yet.

import { ApiError } from "@/lib/api";
import {
  ALERT_TYPES,
  CYCLE_DAY,
  CYCLE_LENGTH,
  DAY_MS,
  DEPARTMENTS,
  DISPLAY_FLEET_SIZE,
  MAX_MOCK_SIM_ID,
  NOW_MS,
  PLAN_TYPES,
  TIER_ORDER,
  TIERS,
  fixtures,
} from "@/lib/mock/fixtures";
import { createRandom } from "@/lib/mock/random";
import { createInitialState, readMockDb, writeMockDb } from "@/lib/mock/db";
import { carrierNameError, normalizePhone, phoneSearchTerm, simFieldErrors } from "@/lib/validation";

const DELAY_MS = 300;
const PAGE_SIZE = 20;
const MAX_PAGE_SIZE = 100;

/**
 * Reference lists for filters and forms. #20: departments from /departments.
 * Carriers change at run time, so they come from useCarriers() instead.
 */
export const referenceData = {
  departments: DEPARTMENTS.map((d) => d.name),
  tiers: TIER_ORDER.map((name) => ({ name, ...TIERS[name] })),
  planTypes: PLAN_TYPES,
  statuses: ["assigned", "available", "inactive"],
  alertTypes: ALERT_TYPES,
  periods: [7, 30, 90],
};

// Dev aid for checking states: add ?mock=error or ?mock=empty to the URL.
function mockMode() {
  if (typeof window === "undefined") return null;
  return new URLSearchParams(window.location.search).get("mock");
}

function wait(ms = DELAY_MS) {
  return new Promise((resolve, reject) => {
    setTimeout(() => {
      if (mockMode() === "error") reject(new ApiError({ status: 500 }));
      else resolve();
    }, ms);
  });
}

function paged(items, page = 1, pageSize = PAGE_SIZE) {
  const size = Math.min(Math.max(Number(pageSize) || PAGE_SIZE, 1), MAX_PAGE_SIZE);
  const total = mockMode() === "empty" ? 0 : items.length;
  const lastPage = Math.max(Math.ceil(total / size), 1);
  const current = Math.min(Math.max(Number(page) || 1, 1), lastPage);
  const start = (current - 1) * size;

  return {
    items: total ? items.slice(start, start + size) : [],
    page: current,
    pageSize: size,
    total,
  };
}

const byNewest = (key) => (a, b) => (a[key] < b[key] ? 1 : a[key] > b[key] ? -1 : 0);
const notFound = () => new ApiError({ status: 404 });
const isOpen = (alert) => alert.status !== "cleared";

function findSim(id) {
  return readMockDb().sims.find((sim) => sim.id === Number(id));
}

/* ------------------------------------------------------------------ SIMs */

/**
 * GET /sims. Search matches number, ICCID or employee name.
 * @param {{ q?: string, status?: string, tier?: string, department?: string,
 *   carrierId?: string | number, page?: number, pageSize?: number }} [query]
 */
export async function listSims({ q, status, tier, department, carrierId, page, pageSize } = {}) {
  await wait();

  const text = q?.trim().toLowerCase();
  const phoneTerm = phoneSearchTerm(q);

  const items = readMockDb().sims.filter((sim) => {
    if (status && sim.status !== status) return false;
    if (tier && sim.tier !== tier) return false;
    if (department && sim.currentAllocation?.departmentName !== department) return false;
    if (carrierId && sim.carrierId !== Number(carrierId)) return false;
    if (!text) return true;

    return (
      sim.phoneNumber.includes(phoneTerm) ||
      sim.iccid.replaceAll(" ", "").includes(text.replaceAll(" ", "")) ||
      Boolean(sim.currentAllocation?.employeeName.toLowerCase().includes(text))
    );
  });

  return paged(items, page, pageSize);
}

/** GET /sims/{id}. Throws ApiError 404 when unknown. */
export async function getSim(id) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();
  return sim;
}

/** GET /alerts with the AlertListItem API shape. */
export async function listAlerts({ status, type, simId, minScore, sort = "createdAt", order = "desc", page, pageSize } = {}) {
  await wait();
  const db = readMockDb();
  const allocationBySim = new Map(db.allocations.filter((item) => !item.returnedAt).map((item) => [item.simId, item]));
  const items = db.alerts
    .filter((alert) => (!status || alert.status === status)
      && (!type || alert.type === type)
      && (!simId || alert.simId === Number(simId))
      && (minScore === undefined || alert.riskScore >= Number(minScore)))
    .map((alert) => {
      const sim = db.sims.find((item) => item.id === alert.simId);
      const allocation = allocationBySim.get(alert.simId);
      return {
        id: alert.id,
        simId: alert.simId,
        simPhoneNumber: sim?.phoneNumber ?? "",
        type: alert.type,
        riskScore: alert.riskScore,
        riskLevel: alert.riskLevel,
        status: alert.status,
        createdAt: alert.createdAt,
        resolvedAt: alert.resolvedAt,
        reasons: alert.reasons,
        modelVersion: alert.modelVersion ?? "2026.10.1",
        employeeName: allocation?.employeeName ?? null,
        departmentName: allocation?.departmentName ?? null,
      };
    })
    .sort((a, b) => {
      const primary = sort === "riskScore" ? a.riskScore - b.riskScore : a.createdAt.localeCompare(b.createdAt);
      const compare = primary || a.id - b.id;
      return order === "asc" ? compare : -compare;
    });
  return paged(items, page, pageSize);
}

/**
 * Server-side guard for /sims/[id]: ids outside the mock range are 404.
 * #20: replace with a GET /sims/{id} on the server.
 */
export function isPossibleSimId(id) {
  const value = Number(id);
  return Number.isInteger(value) && value > 0 && value <= MAX_MOCK_SIM_ID;
}

// future field: usage this billing cycle.
/** Cumulative daily data use this cycle plus voice and SMS balances. */
export async function getSimUsage(id) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();

  const random = createRandom(sim.id * 7919);
  const weights = Array.from({ length: CYCLE_DAY }, () => random.float(0.4, 1.6));
  const sum = weights.reduce((a, b) => a + b, 0);
  let total = 0;

  const series = weights.map((weight, index) => {
    total += (weight / sum) * sim.dataUsedGb;
    return { day: index + 1, gb: Number(total.toFixed(1)) };
  });

  return {
    cycleLength: CYCLE_LENGTH,
    cycleDay: CYCLE_DAY,
    series,
    data: { used: sim.dataUsedGb, limit: TIERS[sim.tier].bundleGb },
    voice: { used: sim.voiceUsed, limit: sim.voiceLimit },
    sms: { used: sim.smsUsed, limit: sim.smsLimit },
  };
}

/** GET /sims/{id}/alerts (future endpoint). */
export async function listSimAlerts(id, { page, pageSize } = {}) {
  await wait();
  const items = readMockDb()
    .alerts.filter((alert) => alert.simId === Number(id))
    .sort(byNewest("createdAt"));
  return paged(items, page, pageSize);
}

/** GET /sims/{id}/allocations. */
export async function listSimAllocations(id, { page, pageSize } = {}) {
  await wait();
  const items = readMockDb()
    .allocations.filter((allocation) => allocation.simId === Number(id))
    .sort(byNewest("assignedAt"));
  return paged(items, page, pageSize);
}

/**
 * Audit trail for one SIM (future endpoint).
 * Items: { id, at, kind, detail } where kind is created, allocated,
 * returned, alert, edited, tier, inactive or available.
 */
export async function listSimActivity(id, { page, pageSize } = {}) {
  await wait();
  const db = readMockDb();
  const simId = Number(id);
  const sim = db.sims.find((s) => s.id === simId);
  if (!sim) throw notFound();

  const events = [{ id: `c${simId}`, at: sim.createdAt, kind: "created" }];

  for (const allocation of db.allocations.filter((a) => a.simId === simId)) {
    events.push({ id: `a${allocation.id}`, at: allocation.assignedAt, kind: "allocated", detail: allocation.employeeName });
    if (allocation.returnedAt) {
      events.push({ id: `r${allocation.id}`, at: allocation.returnedAt, kind: "returned", detail: allocation.employeeName });
    }
  }

  for (const alert of db.alerts.filter((a) => a.simId === simId)) {
    events.push({ id: `l${alert.id}`, at: alert.createdAt, kind: "alert", detail: alert.type });
  }

  events.push(...db.activity.filter((entry) => entry.simId === simId));

  return paged(events.sort(byNewest("at")), page, pageSize);
}

/* ------------------------------------------------------------- Dashboard */

function tierRecommendation(sim) {
  if (sim.status !== "assigned") return "keep";
  const ratio = sim.dataUsedGb / TIERS[sim.tier].bundleGb;
  if (ratio >= 0.9 && sim.tier !== "Field") return "upgrade";
  if (ratio < 0.35 && sim.tier !== "Support") return "downgrade";
  return "keep";
}

const INITIAL_ACTIVE = fixtures.sims.filter((sim) => sim.status !== "inactive").length;

// Share of paid data left unused in each period. Short periods waste more
// because bundles are paid up front and used through the cycle.
const WASTE_RATE = { 7: 0.21, 30: 0.18, 90: 0.16 };

// future field: paid against used data for the period, by department.
function dataWaste(db, days) {
  const random = createRandom(days * 17);
  const rate = WASTE_RATE[days] ?? WASTE_RATE[30];
  const idleByDepartment = new Map();
  let paid = 0;
  let idleTotal = 0;

  for (const sim of db.sims) {
    if (sim.status !== "assigned") continue;
    const bundle = TIERS[sim.tier].bundleGb;
    const idle = Math.max(bundle - sim.dataUsedGb, 0);
    const name = sim.currentAllocation?.departmentName;
    paid += bundle;
    idleTotal += idle;
    if (name) idleByDepartment.set(name, (idleByDepartment.get(name) ?? 0) + idle);
  }

  const paidGb = Math.round((paid * days) / CYCLE_LENGTH);
  const unusedGb = Math.round(paidGb * rate);
  const departments = [...idleByDepartment]
    .map(([name, idle]) => ({
      name,
      unusedGb: Math.round(((unusedGb * idle) / Math.max(idleTotal, 1)) * random.float(0.85, 1.15)),
    }))
    .sort((a, b) => b.unusedGb - a.unusedGb)
    .slice(0, 4);

  return { paidGb, usedGb: paidGb - unusedGb, unusedGb, percent: Math.round(rate * 100), departments };
}

/**
 * Everything the dashboard shows for a period of 7, 30 or 90 days.
 * #20: GET /dashboard?days=30 (future endpoint).
 */
export async function getDashboard(days = 30) {
  await wait();
  return buildDashboard(readMockDb(), days, mockMode() === "empty");
}

/**
 * The dashboard as it first loads, before any edits, built on the server
 * with no delay. The landing page preview uses it so its numbers match
 * the real dashboard. #20: replace with fixed sample numbers.
 */
export function getSampleDashboard(days = 30) {
  return buildDashboard(createInitialState(), days, false);
}

function buildDashboard(db, days, empty) {
  const since = NOW_MS - days * DAY_MS;

  const open = empty
    ? []
    : db.alerts.filter((alert) => isOpen(alert) && Date.parse(alert.createdAt) >= since);

  const active = DISPLAY_FLEET_SIZE - INITIAL_ACTIVE + db.sims.filter((s) => s.status !== "inactive").length;

  const resolved = empty
    ? 0
    : db.alerts.filter((alert) => alert.resolvedAt && Date.parse(alert.resolvedAt) >= since).length;

  const tiers = { upgrade: 0, downgrade: 0 };
  for (const sim of db.sims) {
    const recommendation = tierRecommendation(sim);
    if (recommendation !== "keep") tiers[recommendation] += 1;
  }

  const needsReview = [...open]
    .sort(byNewest("createdAt"))
    .slice(0, 5)
    .map((alert) => {
      const sim = db.sims.find((s) => s.id === alert.simId);
      return { ...alert, phoneNumber: sim.phoneNumber, employeeName: sim.currentAllocation?.employeeName ?? null };
    });

  return {
    days,
    kpis: {
      activeSims: active,
      flagged: open.filter((a) => a.status === "flagged").length,
      toReview: open.length,
      resolved,
    },
    dataWaste: empty
      ? { paidGb: 0, usedGb: 0, unusedGb: 0, percent: 0, departments: [] }
      : dataWaste(db, days),
    alertsByType: ALERT_TYPES.map((type) => ({
      type,
      count: open.filter((a) => a.type === type).length,
    })),
    needsReview,
    tierChanges: empty
      ? { upgrade: 0, downgrade: 0, keep: 0 }
      : { ...tiers, keep: active - tiers.upgrade - tiers.downgrade },
  };
}

/* ------------------------------------------------------------- Mutations */

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

function save(sim, kind, detail) {
  const at = new Date().toISOString();
  writeMockDb({
    type: "sim/saved",
    sim,
    activity: { id: `e${sim.id}-${at}-${kind}`, simId: sim.id, at, kind, detail },
  });
  return sim;
}

/** POST /sims. New SIMs start as "available". */
export async function createSim(body) {
  await wait();
  const values = validateSim(body);
  const db = readMockDb();
  const tier = TIERS.Support;

  return save(
    {
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
    },
    "created"
  );
}

/** PUT /sims/{id}. */
export async function updateSim(id, body) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();
  return save({ ...sim, ...validateSim(body, sim.id) }, "edited");
}

/** PATCH /sims/{id}/status with "available" or "inactive". */
export async function setSimStatus(id, status) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();
  if (!["available", "inactive"].includes(status)) throw fieldError("status", "invalid");
  // Same rule as the API: an allocated SIM must be returned first.
  if (sim.currentAllocation) throw new ApiError({ status: 409 });
  return save({ ...sim, status }, status);
}

// future field: tier changes have no endpoint yet.
/** Changes a SIM's tier and its bundle limits. */
export async function setSimTier(id, tier) {
  await wait();
  const sim = findSim(id);
  if (!sim) throw notFound();
  const limits = TIERS[tier];
  if (!limits) throw fieldError("tier", "invalid");

  return save(
    { ...sim, tier, voiceLimit: limits.voiceMin, smsLimit: limits.sms, monthlyCost: limits.monthlyCost },
    "tier",
    tier
  );
}


/* -------------------------------------------------------------- Carriers */

// future endpoints: carriers are not in the backend yet. Names follow
// carrierNameError: required, trimmed, up to 40 characters, unique
// ignoring case. Errors are ApiError 400 with errors.name = [code].

function carrierName(name, excludeId) {
  const code = carrierNameError(name, readMockDb().carriers, excludeId);
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
