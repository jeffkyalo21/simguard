// The body of POST /allocations. "Handed over by" is optional: it is sent
// only when someone was chosen, so an empty choice is never sent as 0 or NaN.

/**
 * @param {{ simId: number | string, employeeId: number | string,
 *   assignedBy?: number | string | null }} values
 */
export function allocationBody({ simId, employeeId, assignedBy }) {
  const body = { simId: Number(simId), employeeId: Number(employeeId) };
  if (assignedBy !== undefined && assignedBy !== null && assignedBy !== "") body.assignedBy = Number(assignedBy);
  return body;
}
