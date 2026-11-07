// Pure auth helpers shared by the route handlers, the proxy and the sign-in
// screens. No Next.js or browser APIs here, so `node --test` can run them.

/** httpOnly cookie holding the backend access token (the session). */
export const SESSION_COOKIE = "sg_session";

/**
 * Short-lived httpOnly cookie holding the MFA challenge or MFA setup token,
 * stored as "<kind>:<token>".
 */
export const PENDING_COOKIE = "sg_mfa";

export const PENDING_KINDS = Object.freeze({ challenge: "challenge", setup: "setup" });

/** The backend access token lives 60 minutes; the cookie never outlives it. */
export const MAX_SESSION_SECONDS = 60 * 60;

/** The backend MFA token lives 5 minutes. */
export const MAX_PENDING_SECONDS = 5 * 60;

export const ROLES = Object.freeze({
  admin: "Admin",
  securityOfficer: "SecurityOfficer",
  auditor: "Auditor",
});

/**
 * Stable error codes the route handlers return in `{ error: { code } }`.
 * Screens map them to copy; they never show backend text.
 */
export const ERROR_CODES = Object.freeze({
  invalidCredentials: "invalid_credentials",
  mfaInvalid: "mfa_invalid",
  challengeExpired: "challenge_expired",
  rateLimited: "rate_limited",
  validation: "validation",
  noRole: "no_role",
  unauthorized: "unauthorized",
  forbidden: "forbidden",
  notFound: "not_found",
  conflict: "conflict",
  crossOrigin: "cross_origin",
  network: "network",
  server: "server",
});

/** Where each role lands after signing in. */
export function homeFor(role) {
  return role === ROLES.admin ? "/admin" : "/dashboard";
}

export function isLocalHostname(hostname) {
  const host = String(hostname ?? "").toLowerCase();
  return (
    host === "localhost" ||
    host.endsWith(".localhost") ||
    host === "127.0.0.1" ||
    host === "[::1]" ||
    host === "::1"
  );
}

/**
 * Cookie options for the session or pending cookie. Lifetime is the time
 * left on the token, capped at `capSeconds`, so the cookie never outlives it.
 * @param {{ hostname: string, expiresAt?: string, capSeconds: number, now?: number }} input
 */
export function cookieOptions({ hostname, expiresAt, capSeconds, now = Date.now() }) {
  let maxAge = capSeconds;
  const expires = expiresAt ? Date.parse(expiresAt) : NaN;

  if (Number.isFinite(expires)) {
    maxAge = Math.min(capSeconds, Math.floor((expires - now) / 1000));
  }

  return {
    httpOnly: true,
    sameSite: "lax",
    secure: !isLocalHostname(hostname),
    path: "/",
    maxAge: Math.max(0, maxAge),
  };
}

export function encodePending(kind, token) {
  return `${kind}:${token}`;
}

/** @returns {{ kind: string, token: string } | null} */
export function decodePending(value) {
  if (typeof value !== "string") return null;
  const index = value.indexOf(":");
  if (index <= 0) return null;
  const kind = value.slice(0, index);
  const token = value.slice(index + 1);
  if (!Object.values(PENDING_KINDS).includes(kind) || !token) return null;
  return { kind, token };
}

const STATE_CHANGING = new Set(["POST", "PUT", "PATCH", "DELETE"]);

/**
 * True when a state-changing request comes from this site. The Origin
 * header must match the Host header; without Origin, only a browser's
 * `Sec-Fetch-Site: same-origin` is accepted. Safe methods always pass.
 * @param {{ method: string, origin?: string | null, host?: string | null,
 *   secFetchSite?: string | null }} input
 */
export function isSameOriginRequest({ method, origin, host, secFetchSite }) {
  if (!STATE_CHANGING.has(String(method).toUpperCase())) return true;
  if (!host) return false;

  if (!origin) return secFetchSite === "same-origin";
  if (origin === "null") return false;

  try {
    return new URL(origin).host.toLowerCase() === host.toLowerCase();
  } catch {
    return false;
  }
}

const NEXT_BASE = "http://next.invalid";

/**
 * The "next" redirect target, only when it is a relative path on this
 * site. Anything else (absolute URLs, "//host", backslashes, control
 * characters, API or sign-in paths) returns null.
 * @param {unknown} value
 * @returns {string | null}
 */
export function safeNextPath(value) {
  if (typeof value !== "string" || value.length === 0 || value.length > 2048) return null;
  if (!value.startsWith("/") || value.startsWith("//")) return null;
  // Browsers treat "\" like "/", so "/\evil.example" would leave the site.
  if (value.includes("\\")) return null;
  if (/[\u0000-\u001f\u007f]/.test(value)) return null;

  let url;
  try {
    url = new URL(value, NEXT_BASE);
  } catch {
    return null;
  }

  if (url.origin !== NEXT_BASE) return null;
  const path = url.pathname;
  if (path.startsWith("/api/") || path === "/api") return null;
  if (path === "/login" || path.startsWith("/login/")) return null;

  return `${path}${url.search}${url.hash}`;
}

/** Seconds from a Retry-After header (seconds or an HTTP date), or null. */
export function parseRetryAfter(value, now = Date.now()) {
  if (value === null || value === undefined || value === "") return null;
  const seconds = Number(value);
  if (Number.isFinite(seconds)) return Math.max(0, Math.ceil(seconds));
  const date = Date.parse(value);
  if (Number.isFinite(date)) return Math.max(0, Math.ceil((date - now) / 1000));
  return null;
}

const NO_ROLE_DETAIL = "The account has no role";

/**
 * Maps a backend failure to the small, stable shape the screens read:
 * `{ code, fields?, retryAfter? }`. `fields` keeps the ProblemDetails field
 * names (400 only) so a screen can mark the right input.
 * @param {{ status: number, problem?: any, retryAfter?: string | null,
 *   context?: "login" | "mfa" | "enroll" | "session" }} input
 */
export function mapBackendError({ status, problem, retryAfter, context = "session" }) {
  const detail = typeof problem?.detail === "string" ? problem.detail : "";

  if (status === 0) return { code: ERROR_CODES.network };

  if (status === 400) {
    const fields = problem && typeof problem.errors === "object" && problem.errors ? problem.errors : {};
    if (context === "enroll" && fields.code) return { code: ERROR_CODES.mfaInvalid };
    return { code: ERROR_CODES.validation, fields };
  }

  if (status === 401) {
    if (detail.startsWith(NO_ROLE_DETAIL)) return { code: ERROR_CODES.noRole };
    if (context === "login") return { code: ERROR_CODES.invalidCredentials };
    if (context === "mfa") return { code: ERROR_CODES.mfaInvalid };
    if (context === "enroll") return { code: ERROR_CODES.challengeExpired };
    return { code: ERROR_CODES.unauthorized };
  }

  if (status === 403) return { code: ERROR_CODES.forbidden };
  if (status === 404) return { code: ERROR_CODES.notFound };
  if (status === 409) {
    return { code: context === "enroll" ? ERROR_CODES.challengeExpired : ERROR_CODES.conflict };
  }

  if (status === 429) {
    const seconds = parseRetryAfter(retryAfter);
    return seconds === null ? { code: ERROR_CODES.rateLimited } : { code: ERROR_CODES.rateLimited, retryAfter: seconds };
  }

  return { code: ERROR_CODES.server };
}

/**
 * Splits and checks the forwarded path. Rejects empty, "." and ".."
 * segments, and every auth endpoint except the auth event log (tokens
 * only ever travel through /api/auth/*).
 * @param {string[] | undefined} segments
 * @param {string} method
 * @returns {string | null} the backend path after /api/, or null
 */
export function forwardablePath(segments, method) {
  if (!Array.isArray(segments) || segments.length === 0) return null;

  for (const segment of segments) {
    if (!segment || segment === "." || segment === ".." || segment.includes("/") || segment.includes("\\")) {
      return null;
    }
  }

  if (segments[0].toLowerCase() === "auth") {
    const isEvents = segments.length === 2 && segments[1].toLowerCase() === "events";
    if (!isEvents || String(method).toUpperCase() !== "GET") return null;
  }

  return segments.map((segment) => encodeURIComponent(segment)).join("/");
}
