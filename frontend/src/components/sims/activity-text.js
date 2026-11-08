import { copy } from "@/lib/copy";
import { formatDate, formatDateTime } from "@/lib/format";

/**
 * When an activity item happened. An opened alert carries occurredOn, the
 * scored day it was detected; its occurredAt is only when the row was
 * stored, which for backfilled alerts is long after.
 */
export function activityTime(entry) {
  return entry.occurredOn ? formatDate(entry.occurredOn) : formatDateTime(entry.occurredAt);
}

/**
 * Label for one GET /sims/{id}/activity item. Alert and allocation kinds
 * have their own label; audit rows use the audit action label.
 */
export function activityText(entry) {
  const label = copy.simProfile.activity[entry.kind]
    ?? copy.auditLog.actions[entry.action]
    ?? entry.action
    ?? entry.kind;
  const actor = entry.kind === "audit" && entry.actorEmail ? ` ${copy.simProfile.activity.by(entry.actorEmail)}` : "";
  return `${label}${actor}`;
}
