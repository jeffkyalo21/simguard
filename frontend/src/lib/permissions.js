// The single place that says which roles may open which pages and perform
// which actions. The backend enforces the same rules (backend/docs/auth.md,
// "Role matrix"); this map only shapes the UI: navigation, route guards and
// disabled buttons with a reason.
import { ROLES } from "@/lib/auth/core";
import { copy } from "@/lib/copy";

const { admin: ADMIN, securityOfficer: OFFICER, auditor: AUDITOR } = ROLES;

/** Page key -> roles that may open it. */
export const PAGES = Object.freeze({
  dashboard: [OFFICER, AUDITOR],
  usage: [OFFICER, AUDITOR],
  sims: [OFFICER, AUDITOR],
  allocations: [OFFICER, AUDITOR],
  alerts: [OFFICER, AUDITOR],
  // Carriers and departments are tabs of Settings; the Auditor opens
  // Settings to read them but cannot change anything there.
  settings: [OFFICER, AUDITOR],
  carriers: [OFFICER, AUDITOR],
  departments: [OFFICER, AUDITOR],
  imports: [OFFICER],
  // The Auditor reads the recommendations; only the Security Officer acts.
  tierRecommendations: [OFFICER, AUDITOR],
  // The Admin sees only the system entries (the backend filters them).
  auditLog: [AUDITOR, ADMIN],
  admin: [ADMIN],
});

/** First path segment -> page key. Paths not listed here are not guarded. */
export const PAGE_PATHS = Object.freeze({
  dashboard: "dashboard",
  usage: "usage",
  sims: "sims",
  allocations: "allocations",
  alerts: "alerts",
  settings: "settings",
  imports: "imports",
  "tier-recommendations": "tierRecommendations",
  "audit-log": "auditLog",
  admin: "admin",
});

/** Action key -> roles that may perform it. */
export const ACTIONS = Object.freeze({
  "sims.edit": [OFFICER],
  "sims.changeStatus": [OFFICER],
  "sims.changeTier": [OFFICER],
  "allocations.edit": [OFFICER],
  "alerts.review": [OFFICER],
  "carriers.edit": [OFFICER],
  "departments.edit": [OFFICER],
  "employees.edit": [OFFICER],
  "imports.run": [OFFICER],
  "tierRecommendations.edit": [OFFICER],
  "settings.edit": [OFFICER],
  "auditLog.view": [AUDITOR, ADMIN],
  "users.manage": [ADMIN],
  "scoring.run": [ADMIN],
});

/** True when `role` may open the page with this key. */
export function canOpen(role, page) {
  return PAGES[page]?.includes(role) ?? false;
}

/** The page key a path belongs to, or null for paths outside the map. */
export function pageForPath(pathname) {
  const segment = String(pathname ?? "").split(/[?#]/)[0].split("/")[1] ?? "";
  return PAGE_PATHS[segment] ?? null;
}

/**
 * True when `role` may open this path. Paths outside the map (for example
 * an unknown URL that will show "Page not found") are not blocked here.
 */
export function canOpenPath(role, pathname) {
  const page = pageForPath(pathname);
  return page === null ? true : canOpen(role, page);
}

/** True when `role` may perform the action with this key. */
export function can(role, action) {
  return ACTIONS[action]?.includes(role) ?? false;
}

/**
 * Why a disabled control is disabled, for DisabledReason. Null when the
 * role may perform the action.
 */
export function deniedReason(role, action) {
  if (can(role, action)) return null;
  return role === AUDITOR ? copy.permissions.readOnly : copy.permissions.notAllowed;
}
