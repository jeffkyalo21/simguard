// TEMPORARY (issue #17): fictional, deterministic mock data. Removed when
// src/lib/data.js switches to the API in #20. Every person is invented.
//
// Records follow the backend shapes (SimResponse, allocation and alert
// rows). Fields the backend does not have yet sit in blocks marked
// "// future field".

import { createRandom } from "@/lib/mock/random";

/** Fixed "now" for the mock world, so nothing depends on the clock. */
export const MOCK_NOW = "2026-10-29T06:00:00Z";
export const NOW_MS = Date.parse(MOCK_NOW);
export const DAY_MS = 86_400_000;

/** Billing cycle: day 29 of 30. */
export const CYCLE_LENGTH = 30;
export const CYCLE_DAY = 29;

/** The dashboard shows a fleet of 1,000 while the list holds ~120 rows. */
export const DISPLAY_FLEET_SIZE = 1000;

export const DEPARTMENTS = [
  { id: 1, name: "Flight ops" },
  { id: 2, name: "Ground handling" },
  { id: 3, name: "Finance" },
  { id: 4, name: "IT" },
  { id: 5, name: "Cargo" },
  { id: 6, name: "Customer care" },
];

// future field: tiers do not exist in the backend yet.
export const TIERS = {
  Support: { bundleGb: 20, voiceMin: 200, sms: 50, monthlyCost: 1500 },
  Professional: { bundleGb: 40, voiceMin: 300, sms: 100, monthlyCost: 2500 },
  Manager: { bundleGb: 60, voiceMin: 500, sms: 200, monthlyCost: 3500 },
  Field: { bundleGb: 100, voiceMin: 1000, sms: 300, monthlyCost: 5000 },
};
export const TIER_ORDER = ["Support", "Professional", "Manager", "Field"];

/**
 * Mobile carriers the organisation buys SIMs from. The only place carrier
 * names are written; screens look them up by id from the mock store.
 * status is "connected" or "not connected".
 */
export const CARRIERS = [
  { id: 1, name: "Safaricom", status: "connected", createdAt: "2025-05-12T08:00:00Z" },
  { id: 2, name: "Airtel", status: "not connected", createdAt: "2025-05-12T08:05:00Z" },
  { id: 3, name: "Telkom", status: "not connected", createdAt: "2025-05-12T08:10:00Z" },
];
export const PLAN_TYPES = ["Corporate postpaid", "Data bundle", "Prepaid"];
export const ALERT_TYPES = ["inactiveBilling", "sharing", "reselling", "ghostUsage"];

const FIRST_NAMES = [
  "Akinyi", "Wambui", "Omondi", "Chebet", "Muthoni", "Kibet", "Nafula",
  "Wekesa", "Auma", "Onyango", "Nyambura", "Mwende", "Wairimu", "Ochieng",
  "Atieno", "Rotich", "Cherono", "Halima", "Fatuma", "Juma", "Zawadi",
  "Imani", "Makena", "Gitau", "Karanja", "Nduta", "Simiyu", "Langat",
  "Kerubo", "Moraa", "Nyaboke", "Abdi", "Wafula", "Mumbua", "Jelagat",
];

const LAST_NAMES = [
  "Njoroge", "Mugo", "Waweru", "Wanjala", "Barasa", "Korir", "Mutua",
  "Musyoka", "Mwikali", "Ndunge", "Ongeri", "Ali", "Otieno", "Kariuki",
  "Kimani", "Owino", "Cheruiyot", "Nyongesa", "Maina", "Omollo", "Wangari",
  "Kiprotich", "Mohamed", "Achola", "Ngugi",
];

const PHONE_PREFIXES = ["70", "71", "72", "74", "79", "11", "10"];

// The first rows match the wireframes, so the same SIM and person look the
// same on the list, the profile and the dashboard.
const FEATURED = [
  { phone: "+254712345678", name: "Wanjiru Kamau", dept: "Finance", tier: "Professional", used: 28, voice: 120, sms: 40, assignedAt: "2026-03-03T07:30:00Z" },
  { phone: "+254722118904", name: "Otieno Ouma", dept: "Flight ops", tier: "Field", used: 92, voice: 640, sms: 120, assignedAt: "2026-01-19T08:10:00Z" },
  { phone: "+254733560221", name: "Achieng Odhiambo", dept: "Ground handling", tier: "Support", used: 7, voice: 90, sms: 12, assignedAt: "2026-04-14T06:45:00Z" },
  { phone: "+254701889342", name: "Kiprono Koech", dept: "IT", tier: "Manager", used: 3, voice: 15, sms: 4, assignedAt: "2025-11-02T09:00:00Z" },
  { phone: "+254745003176", name: "Amina Hassan", dept: "Cargo", tier: "Support", used: 16, voice: 150, sms: 31, assignedAt: "2026-02-08T10:20:00Z" },
  { phone: "+254718440095", name: "Baraka Mutua", dept: "Customer care", tier: "Professional", used: 19, voice: 210, sms: 66, assignedAt: "2026-05-21T07:00:00Z" },
  { phone: "+254729671308", name: "Njeri Mwangi", dept: "Finance", tier: "Manager", used: 13, voice: 180, sms: 70, assignedAt: "2026-06-11T11:15:00Z" },
  { phone: "+254704912550", name: "Mutiso Kioko", dept: "Flight ops", tier: "Field", used: 61, voice: 520, sms: 140, assignedAt: "2026-07-01T05:30:00Z" },
];

const SIM_COUNT = 120;
const INACTIVE_INDEXES = new Set([14, 33, 52, 71, 90, 109]);
const AVAILABLE_INDEXES = new Set([21, 46, 64, 83, 101, 117]);

const iso = (ms) => new Date(ms).toISOString();
const daysAgo = (days, hour = 6) => NOW_MS - days * DAY_MS - (6 - hour) * 3_600_000;

function shuffle(items, random) {
  const copy = [...items];
  for (let i = copy.length - 1; i > 0; i -= 1) {
    const j = random.int(0, i);
    [copy[i], copy[j]] = [copy[j], copy[i]];
  }
  return copy;
}

function buildFixtures() {
  const random = createRandom(20261029);
  const departmentByName = new Map(DEPARTMENTS.map((d) => [d.name, d]));

  // Employees: featured people first, then unique generated names.
  const employees = [];
  const usedNames = new Set();

  function addEmployee(name, departmentName) {
    const employee = {
      id: employees.length + 1,
      name,
      departmentId: departmentByName.get(departmentName).id,
      departmentName,
    };
    employees.push(employee);
    usedNames.add(name);
    return employee;
  }

  function newEmployee() {
    let name;
    do {
      name = `${random.pick(FIRST_NAMES)} ${random.pick(LAST_NAMES)}`;
    } while (usedNames.has(name));
    return addEmployee(name, random.pick(DEPARTMENTS).name);
  }

  // Phone numbers stay unique.
  const usedPhones = new Set(FEATURED.map((f) => f.phone));
  function newPhone() {
    let phone;
    do {
      const digits = String(random.int(0, 9_999_999)).padStart(7, "0");
      phone = `+254${random.pick(PHONE_PREFIXES)}${digits}`;
    } while (usedPhones.has(phone));
    usedPhones.add(phone);
    return phone;
  }

  // Decide which generated, assigned SIMs need a tier change: exactly 12
  // upgrades and 29 downgrades (plus 2 downgrades among the featured rows),
  // matching the dashboard wireframe's 12 and 31.
  const assignedGenerated = [];
  for (let i = FEATURED.length; i < SIM_COUNT; i += 1) {
    if (!INACTIVE_INDEXES.has(i) && !AVAILABLE_INDEXES.has(i)) assignedGenerated.push(i);
  }
  const order = shuffle(assignedGenerated, random);
  const upgradeSet = new Set(order.slice(0, 12));
  const downgradeSet = new Set(order.slice(12, 41));

  const sims = [];
  const allocations = [];

  function addAllocation(simId, employee, assignedAt, returnedAt = null) {
    const allocation = {
      id: allocations.length + 1,
      simId,
      employeeId: employee.id,
      employeeName: employee.name,
      departmentName: employee.departmentName,
      assignedAt,
      returnedAt,
    };
    allocations.push(allocation);
    return allocation;
  }

  for (let i = 0; i < SIM_COUNT; i += 1) {
    const id = i + 1;
    const featured = FEATURED[i];
    const createdMs = Date.parse("2025-06-02T07:00:00Z") + random.int(0, 140) * DAY_MS;

    let status = "assigned";
    if (INACTIVE_INDEXES.has(i)) status = "inactive";
    if (AVAILABLE_INDEXES.has(i)) status = "available";

    let tier = featured?.tier;
    if (!tier) {
      const roll = random.next();
      tier = roll < 0.35 ? "Support" : roll < 0.7 ? "Professional" : roll < 0.85 ? "Manager" : "Field";
    }
    if (downgradeSet.has(i) && tier === "Support") tier = "Professional";
    if (upgradeSet.has(i) && tier === "Field") tier = "Manager";
    if (status === "available") tier = "Support";

    const limits = TIERS[tier];
    let dataUsedGb = 0;
    let voiceUsed = 0;
    let smsUsed = 0;

    if (featured) {
      dataUsedGb = featured.used;
      voiceUsed = featured.voice;
      smsUsed = featured.sms;
    } else if (status === "assigned") {
      const ratio = upgradeSet.has(i)
        ? random.float(0.92, 1.08)
        : downgradeSet.has(i)
          ? random.float(0.08, 0.33)
          : random.float(0.36, 0.85);
      dataUsedGb = Math.round(ratio * limits.bundleGb);
      voiceUsed = Math.round(random.float(0.1, 0.9) * limits.voiceMin);
      smsUsed = Math.round(random.float(0.05, 0.9) * limits.sms);
    }

    let currentAllocation = null;

    if (status === "assigned") {
      const employee = featured ? addEmployee(featured.name, featured.dept) : newEmployee();
      const assignedMs = featured
        ? Date.parse(featured.assignedAt)
        : Math.min(createdMs + random.int(20, 200) * DAY_MS, NOW_MS - 10 * DAY_MS);

      // Some SIMs had an earlier holder.
      if (!featured && random.next() < 0.3) {
        addAllocation(id, newEmployee(), iso(createdMs + DAY_MS), iso(assignedMs - DAY_MS));
      }

      const allocation = addAllocation(id, employee, iso(assignedMs));
      currentAllocation = {
        allocationId: allocation.id,
        employeeId: employee.id,
        employeeName: employee.name,
        departmentName: employee.departmentName,
        assignedAt: allocation.assignedAt,
      };
    } else if (status === "inactive") {
      addAllocation(id, newEmployee(), iso(createdMs + DAY_MS), iso(createdMs + 120 * DAY_MS));
    }

    sims.push({
      id,
      phoneNumber: featured?.phone ?? newPhone(),
      iccid: `8925 4010 0000 ${String(12 + i * 13).padStart(4, "0")}`,
      carrierId: i === 0 ? CARRIERS[0].id : random.pick(CARRIERS).id,
      planType: random.pick(PLAN_TYPES),
      monthlyCost: limits.monthlyCost,
      status,
      createdAt: iso(createdMs),
      currentAllocation,

      // future field: tier, usage and risk are not in the backend yet.
      tier,
      dataUsedGb,
      voiceUsed,
      voiceLimit: limits.voiceMin,
      smsUsed,
      smsLimit: limits.sms,
      riskScore: 0,
    });
  }

  const alerts = buildAlerts(sims, random);

  // A SIM's risk is its highest open alert, otherwise a low baseline.
  for (const sim of sims) {
    const open = alerts.filter((a) => a.simId === sim.id && a.status !== "cleared");
    sim.riskScore = open.length
      ? Math.max(...open.map((a) => a.riskScore))
      : Number(random.float(0.04, 0.38).toFixed(2));
  }

  // The signed-in user is also the employee who records allocations
  // (the API's assignedBy). Added last so no other id moves.
  const assigner = addEmployee(MOCK_USER.name, "IT");
  for (const allocation of allocations) allocation.assignedBy = assigner.id;

  const auditLogs = buildAuditLogs({ sims, allocations, alerts });

  return { employees, assignerId: assigner.id, carriers: CARRIERS, sims, allocations, alerts, auditLogs };
}

/** The signed-in user in the mock world. #20: comes from the access token. */
export const MOCK_USER = { id: 1, name: "Jeff", email: "jeff@simguard.local" };

const AUDIT_ACTORS = [
  { id: MOCK_USER.id, email: MOCK_USER.email },
  { id: MOCK_USER.id, email: MOCK_USER.email },
  { id: 2, email: "officer@simguard.local" },
  { id: 3, email: "njeri.mwangi@simguard.local" },
];
const AUDIT_ACTIONS = ["sim.created", "sim.status_changed", "allocation.created", "allocation.returned", "alert.cleared", "alert.reopened"];
const AUDIT_ROWS = 40;

const carrierById = (id) => CARRIERS.find((carrier) => carrier.id === id)?.name ?? null;

const simSnapshot = (sim, status) => ({
  id: sim.id,
  phoneNumber: sim.phoneNumber,
  iccid: sim.iccid,
  operator: carrierById(sim.carrierId),
  planType: sim.planType,
  monthlyCost: sim.monthlyCost,
  status,
});

const alertSnapshot = (alert, status, resolution = null, resolvedAt = null, note = null) => ({
  alertId: alert.id,
  status,
  resolution,
  resolutionNote: note,
  resolvedAt,
  resolvedByUserId: resolution ? MOCK_USER.id : null,
});

// About 40 audit rows over the last 90 days, in the GET /audit-logs item
// shape. A separate seed, so the other fixtures stay as they were.
function buildAuditLogs({ sims, allocations, alerts }) {
  const random = createRandom(19190);
  const assigned = sims.filter((sim) => sim.currentAllocation);
  const unassigned = sims.filter((sim) => !sim.currentAllocation);
  const ended = allocations.filter((allocation) => allocation.returnedAt);
  const cleared = alerts.filter((alert) => alert.status === "cleared");
  const open = alerts.filter((alert) => alert.status !== "cleared");
  const simById = new Map(sims.map((sim) => [sim.id, sim]));

  const rows = Array.from({ length: AUDIT_ROWS }, (_, index) => {
    const kind = AUDIT_ACTIONS[index % AUDIT_ACTIONS.length];
    const timestamp = iso(Math.min(daysAgo(random.int(0, 89), random.int(0, 11)) - random.int(0, 59) * 60_000, NOW_MS));
    let action = kind;
    let sim;
    let oldValues = null;
    let newValues = null;

    if (kind === "sim.created") {
      sim = random.pick(unassigned);
      newValues = simSnapshot(sim, "available");
    } else if (kind === "sim.status_changed") {
      sim = random.pick(unassigned);
      const toInactive = random.next() < 0.5;
      oldValues = { status: toInactive ? "available" : "inactive" };
      newValues = { status: toInactive ? "inactive" : "available" };
    } else if (kind === "allocation.created") {
      sim = random.pick(assigned);
      const allocation = sim.currentAllocation;
      oldValues = { simStatus: "available" };
      newValues = { allocationId: allocation.allocationId, employeeId: allocation.employeeId, assignedBy: allocations[0].assignedBy, assignmentDate: timestamp, simStatus: "assigned" };
    } else if (kind === "allocation.returned") {
      const allocation = random.pick(ended);
      sim = simById.get(allocation.simId);
      oldValues = { allocationId: allocation.id, returnDate: null, simStatus: "assigned" };
      newValues = { allocationId: allocation.id, returnDate: timestamp, simStatus: "available" };
    } else if (kind === "alert.cleared") {
      const alert = random.pick(cleared);
      const resolution = random.next() < 0.6 ? "falsePositive" : "confirmedMisuse";
      sim = simById.get(alert.simId);
      action = "alert.status_changed";
      oldValues = alertSnapshot(alert, random.next() < 0.5 ? "flagged" : "escalated");
      newValues = alertSnapshot(alert, "cleared", resolution, timestamp, resolution === "falsePositive" ? "Activity explained by the employee." : "Misuse confirmed with the line manager.");
    } else {
      const alert = random.pick(open);
      sim = simById.get(alert.simId);
      action = "alert.status_changed";
      const clearedAt = iso(Date.parse(timestamp) - random.int(1, 3) * DAY_MS);
      oldValues = alertSnapshot(alert, "cleared", "falsePositive", clearedAt, "Activity explained by the employee.");
      newValues = alertSnapshot(alert, "escalated");
    }

    const actor = random.pick(AUDIT_ACTORS);

    return {
      timestamp,
      actorUserId: actor.id,
      actorEmail: actor.email,
      employeeId: null,
      action,
      affectedSimId: sim.id,
      affectedSimPhoneNumber: sim.phoneNumber,
      oldValues,
      newValues,
    };
  });

  return rows
    .sort((a, b) => a.timestamp.localeCompare(b.timestamp))
    .map((row, index) => ({ id: index + 1, ...row }));
}

// Open alerts in the last 30 days total 62 (23, 18, 12, 9 by type), 14 of
// them flagged, matching the dashboard wireframe. Older and cleared alerts
// fill the 90-day view and the SIM profile.
function buildAlerts(sims, random) {
  const alerts = [];
  const assignedIds = sims.filter((s) => s.status === "assigned" && s.id > 8).map((s) => s.id);

  function add(simId, type, riskScore, status, createdMs) {
    const featureSets = {
      inactiveBilling: [
        ["billing_ratio", 0.82, 0.36, "up"],
        ["total_mb", 0.24, 0.27, "down"],
        ["mean_session_duration_s", 0.31, 0.19, "down"],
      ],
      sharing: [
        ["hotspot_session_ratio", 0.76, 0.36, "up"],
        ["hotspot_sum_7d", 0.42, 0.27, "up"],
        ["hotspot_sessions", 0.33, 0.19, "up"],
      ],
      reselling: [
        ["offpeak_ratio", 0.79, 0.36, "up"],
        ["offpeak_mb", 0.47, 0.27, "up"],
        ["peak_mb", 0.28, 0.19, "down"],
      ],
      ghostUsage: [
        ["total_mb", 0.84, 0.36, "up"],
        ["bytes_per_session", 0.59, 0.27, "up"],
        ["cohort_z_total_mb", 0.42, 0.19, "up"],
      ],
    };
    alerts.push({
      id: alerts.length + 1,
      simId,
      type,
      riskScore: Number(riskScore.toFixed(2)),
      riskLevel: status === "flagged" ? "high" : status === "escalated" ? "medium" : riskScore >= 0.9 ? "high" : "medium",
      status,
      createdAt: iso(createdMs),
      modelVersion: "2026.10.1",
      reasons: featureSets[type].map(([feature, value, contribution, direction]) => ({ feature, value, contribution, direction })),
      // Cleared alerts close two days after they open (never after "now").
      resolvedAt: status === "cleared" ? iso(Math.min(createdMs + 2 * DAY_MS, NOW_MS)) : null,
    });
  }

  // Most recent five: the "Needs review" rows on the wireframe.
  add(1, "sharing", 0.94, "flagged", daysAgo(1, 14));
  add(2, "reselling", 0.88, "flagged", daysAgo(1, 9));
  add(3, "ghostUsage", 0.71, "escalated", daysAgo(2, 16));
  add(4, "inactiveBilling", 0.64, "escalated", daysAgo(2, 11));
  add(5, "sharing", 0.52, "escalated", daysAgo(3, 15));
  add(1, "sharing", 0.58, "escalated", daysAgo(29, 10));

  // The other 56 open alerts in the last 30 days.
  const types = shuffle(
    [
      ...Array(22).fill("inactiveBilling"),
      ...Array(15).fill("sharing"),
      ...Array(11).fill("reselling"),
      ...Array(8).fill("ghostUsage"),
    ],
    random
  );
  types.forEach((type, index) => {
    const flagged = index < 12;
    add(
      random.pick(assignedIds),
      type,
      flagged ? random.float(0.8, 0.97) : random.float(0.5, 0.79),
      flagged ? "flagged" : "escalated",
      daysAgo(random.int(3, 28), random.int(0, 5))
    );
  });

  // Older open alerts for the 90-day view.
  for (let i = 0; i < 30; i += 1) {
    const flagged = random.next() < 0.2;
    add(
      random.pick(assignedIds),
      random.pick(ALERT_TYPES),
      flagged ? random.float(0.8, 0.95) : random.float(0.5, 0.79),
      flagged ? "flagged" : "escalated",
      daysAgo(random.int(31, 88), random.int(0, 5))
    );
  }

  // Cleared alerts.
  add(1, "ghostUsage", 0.71, "cleared", daysAgo(17, 12));
  for (let i = 0; i < 24; i += 1) {
    add(
      random.pick(assignedIds),
      random.pick(ALERT_TYPES),
      random.float(0.3, 0.7),
      "cleared",
      daysAgo(random.int(1, 88), random.int(0, 5))
    );
  }

  return alerts;
}

export const fixtures = buildFixtures();

/** Highest id the mock world can hold: fixtures plus room for added SIMs. */
export const MAX_MOCK_SIM_ID = SIM_COUNT + 500;
