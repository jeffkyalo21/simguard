// Browser side of sign-in: calls the /api/auth route handlers (never the
// backend, never a token) and turns their error codes into copy.
import { ERROR_CODES, homeFor, safeNextPath } from "@/lib/auth/core";
import { copy } from "@/lib/copy";
import { canOpenPath } from "@/lib/permissions";

/**
 * POSTs (or GETs) to an /api/auth route handler.
 * @param {string} path  for example "/api/auth/login"
 * @param {unknown} [body]
 * @returns {Promise<{ ok: true, data: any } | { ok: false, error: { code: string,
 *   fields?: Record<string, string[]>, retryAfter?: number } }>}
 */
export async function authRequest(path, body, { method = "POST" } = {}) {
  let response;

  try {
    response = await fetch(path, {
      method,
      credentials: "same-origin",
      cache: "no-store",
      headers: {
        Accept: "application/json",
        ...(body !== undefined && { "Content-Type": "application/json" }),
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    return { ok: false, error: { code: ERROR_CODES.network } };
  }

  let data;
  try {
    data = response.status === 204 ? undefined : await response.json();
  } catch {
    data = undefined;
  }

  if (response.ok) return { ok: true, data };
  return { ok: false, error: data?.error?.code ? data.error : { code: ERROR_CODES.server } };
}

/** Ends the session (drops both cookies). */
export function signOutRequest() {
  return authRequest("/api/auth/logout");
}

/**
 * The short message for an error code. `recovery` words a failed recovery
 * code differently from a failed app code.
 */
export function authErrorText(error, { recovery = false } = {}) {
  const text = copy.auth.errors;

  switch (error?.code) {
    case ERROR_CODES.invalidCredentials:
      return text.invalidCredentials;
    case ERROR_CODES.mfaInvalid:
      return recovery ? text.recoveryInvalid : text.mfaInvalid;
    case ERROR_CODES.challengeExpired:
      return copy.login.reasons.restart;
    case ERROR_CODES.rateLimited:
      return typeof error.retryAfter === "number" && error.retryAfter > 0
        ? text.rateLimitedFor(error.retryAfter)
        : text.rateLimited;
    case ERROR_CODES.noRole:
      return text.noRole;
    case ERROR_CODES.unauthorized:
      return text.sessionExpired;
    case ERROR_CODES.validation:
      return copy.errors.validation;
    case ERROR_CODES.network:
      return copy.errors.network;
    case ERROR_CODES.server:
      return copy.errors.server;
    default:
      return copy.errors.generic;
  }
}

/** The public landing page, where a deleted account ends up. */
export function landingHref() {
  return "/";
}

/** Query string carrying a safe "next" path between the sign-in steps. */
export function nextQuery(next) {
  const safe = safeNextPath(next);
  return safe ? `?next=${encodeURIComponent(safe)}` : "";
}

/** /login with an optional safe "next" path and a reason notice. */
export function loginHref({ next, reason } = {}) {
  const params = new URLSearchParams();
  const safe = safeNextPath(next);
  if (safe) params.set("next", safe);
  if (reason) params.set("reason", reason);
  const query = params.toString();
  return `/login${query ? `?${query}` : ""}`;
}

/**
 * Where to go once the backend has issued a session: the forced password
 * change first, then the requested page, else the role's home.
 * @param {{ role: string, mustChangePassword?: boolean, next?: string | null }} input
 */
export function destinationAfterSignIn({ role, mustChangePassword, next }) {
  if (mustChangePassword) return `/login/change-password${nextQuery(next)}`;
  const safe = safeNextPath(next);
  // A "next" page outside the role (an Auditor's old /imports link, say)
  // would only show "no access", so the role's home is used instead.
  return safe && canOpenPath(role, safe) ? safe : homeFor(role);
}
