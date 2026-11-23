// How an alert is shown: flagged and escalated as they are, a cleared alert as
// "confirmed" (misuse was confirmed) or "cleared" (dismissed as normal use).
// Pure, so `node --test` runs it.
import { copy } from "@/lib/copy";

/** The filter options of the alerts list, in display order. */
export const OUTCOME_FILTERS = ["flagged", "escalated", "confirmed", "cleared"];

/**
 * @param {{ status?: string, resolution?: string | null } | null | undefined} alert
 * @returns {"flagged" | "escalated" | "confirmed" | "cleared"}
 */
export function alertOutcome(alert) {
  if (alert?.status === "flagged") return "flagged";
  if (alert?.status === "cleared") return alert.resolution === "confirmedMisuse" ? "confirmed" : "cleared";
  return "escalated";
}

/**
 * The GET /alerts query for a filter option. Both closed outcomes send
 * status=cleared with the matching resolution.
 * @param {string | undefined} option
 * @returns {{ status?: string, resolution?: string }}
 */
export function outcomeQuery(option) {
  switch (option) {
    case "flagged":
      return { status: "flagged" };
    case "escalated":
      return { status: "escalated" };
    case "confirmed":
      return { status: "cleared", resolution: "confirmedMisuse" };
    case "cleared":
      return { status: "cleared", resolution: "falsePositive" };
    default:
      return {};
  }
}

/**
 * Audit label for a row. A status change to cleared says whether misuse was
 * confirmed (newValues.resolution); the other closed outcome keeps "Alert cleared".
 */
export function auditActionLabel(row) {
  const actions = copy.auditLog.actions;
  if (row.action === "alert.status_changed") {
    if (row.newValues?.status === "cleared") {
      return row.newValues.resolution === "confirmedMisuse" ? actions.alertConfirmed : actions.alertCleared;
    }
    if (row.oldValues?.status === "cleared") return actions.alertReopened;
  }
  return actions[row.action] ?? row.action;
}
