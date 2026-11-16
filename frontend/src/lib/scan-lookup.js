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
