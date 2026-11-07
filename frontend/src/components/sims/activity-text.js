import { copy } from "@/lib/copy";

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
