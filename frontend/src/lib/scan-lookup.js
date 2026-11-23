// What a scanned ICCID means for the allocation form. Pure; the caller passes
// the SIMs the list API returned for the scan.

/**
 * @param {string} iccid a checked ICCID
 * @param {{ iccid: string, status: string, id: number }[]} sims
 * @returns {{ kind: "available" | "allocated" | "inactive" | "unregistered", sim: object | null }}
 */
export function classifyScannedSim(iccid, sims) {
  const sim = sims.find((item) => item.iccid === iccid) ?? null;
  if (!sim) return { kind: "unregistered", sim: null };
  if (sim.status === "available") return { kind: "available", sim };
  if (sim.status === "assigned") return { kind: "allocated", sim };
  return { kind: "inactive", sim };
}

/**
 * The SIM that already has this ICCID, or null. The match is exact: a search
 * hit on a phone number or a longer number is not a duplicate. `ownId` is the
 * SIM being edited, which never duplicates itself.
 * @param {string} iccid a scanned or typed ICCID
 * @param {{ iccid: string, id: number }[]} sims what the list API returned for it
 * @param {number | null} [ownId]
 */
export function findRegisteredSim(iccid, sims, ownId = null) {
  const wanted = String(iccid ?? "").trim();
  if (!wanted) return null;
  return sims.find((item) => item.iccid === wanted && item.id !== ownId) ?? null;
}

/**
 * The add-SIM fields after "Save and scan next": the carrier stays, everything
 * that belongs to one SIM is cleared.
 * @param {{ carrierId: string }} values
 */
export function nextIntakeValues(values) {
  return { phoneNumber: "", iccid: "", carrierId: values.carrierId, planType: "", monthlyCost: "" };
}

/** The form value for the SIM select: its id as text. */
export function simFieldValue(sim) {
  return String(sim.id);
}

/**
 * The options of the SIM select with a scanned or just registered SIM added
 * when the first page of available SIMs does not hold it.
 */
export function withExtraSim(options, sim, label) {
  const value = simFieldValue(sim);
  return options.some((option) => option.value === value) ? options : [{ value, label }, ...options];
}
