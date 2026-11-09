import "server-only";
import { cookies } from "next/headers";
import {
  ERROR_CODES,
  MAX_PENDING_SECONDS,
  MAX_SESSION_SECONDS,
  PENDING_COOKIE,
  SESSION_COOKIE,
  cookieOptions,
  decodePending,
  encodePending,
  isSameOriginRequest,
  mapBackendError,
} from "@/lib/auth/core";

const BACKEND_TIMEOUT_MS = 15_000;

/** Base URL of the SimGuard API, from the server-only BACKEND_URL. */
export function backendUrl() {
  const value = process.env.BACKEND_URL;
  if (!value) throw new Error("BACKEND_URL is not set.");
  return value.replace(/\/+$/, "");
}

async function readJson(response) {
  const text = await response.text();
  if (!text) return undefined;
  try {
    return JSON.parse(text);
  } catch {
    return undefined;
  }
}

/**
 * Headers that hand the visitor's address to the API as X-Forwarded-For, so
 * its per-IP rate limits count visitors one by one and not this server as a
 * whole. The address is the last entry of X-Forwarded-For, which the proxy in
 * front of this server (Caddy) set. Empty when the header is missing (local
 * development without a proxy).
 * @param {Request} request
 * @returns {Record<string, string>}
 */
export function visitorAddressHeaders(request) {
  const last = request.headers.get("x-forwarded-for")?.split(",").at(-1)?.trim();
  return last ? { "X-Forwarded-For": last } : {};
}

/**
 * Calls the backend. Never throws for HTTP or network failures: returns
 * `{ ok, status, data, retryAfter }` with status 0 when the backend could
 * not be reached. Tokens are sent as a Bearer header and never logged.
 * @param {string} path  relative to /api, for example "/auth/login"
 * @param {{ method?: string, token?: string, body?: unknown,
 *   headers?: Record<string, string> }} [options]
 */
export async function callBackend(path, { method = "GET", token, body, headers } = {}) {
  let response;

  try {
    response = await fetch(`${backendUrl()}/api${path}`, {
      method,
      cache: "no-store",
      signal: AbortSignal.timeout(BACKEND_TIMEOUT_MS),
      headers: {
        Accept: "application/json",
        ...(body !== undefined && { "Content-Type": "application/json" }),
        ...(token && { Authorization: `Bearer ${token}` }),
        ...headers,
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch {
    return { ok: false, status: 0, data: undefined, retryAfter: null };
  }

  return {
    ok: response.ok,
    status: response.status,
    data: await readJson(response),
    retryAfter: response.headers.get("retry-after"),
  };
}

/** JSON response in the `{ error: { code, ... } }` shape the screens read. */
export function errorResponse(error, status) {
  const httpStatus = status ?? statusForCode(error.code);
  return Response.json({ error }, { status: httpStatus, headers: { "Cache-Control": "no-store" } });
}

function statusForCode(code) {
  switch (code) {
    case ERROR_CODES.validation:
    case ERROR_CODES.mfaInvalid:
      return 400;
    case ERROR_CODES.invalidCredentials:
    case ERROR_CODES.challengeExpired:
    case ERROR_CODES.unauthorized:
    case ERROR_CODES.noRole:
      return 401;
    case ERROR_CODES.forbidden:
    case ERROR_CODES.crossOrigin:
      return 403;
    case ERROR_CODES.notFound:
      return 404;
    case ERROR_CODES.conflict:
      return 409;
    case ERROR_CODES.rateLimited:
      return 429;
    case ERROR_CODES.network:
      return 502;
    default:
      return 500;
  }
}

/** Maps a failed `callBackend` result to an error response. */
export function backendErrorResponse(result, context) {
  const error = mapBackendError({
    status: result.status,
    problem: result.data,
    retryAfter: result.retryAfter,
    context,
  });
  return errorResponse(error);
}

export function json(data, status = 200) {
  return Response.json(data, { status, headers: { "Cache-Control": "no-store" } });
}

/**
 * Rejects cross-origin state-changing requests. Returns an error response
 * to send back, or null when the request may go on.
 * @param {Request} request
 */
export function rejectCrossOrigin(request) {
  const allowed = isSameOriginRequest({
    method: request.method,
    origin: request.headers.get("origin"),
    host: request.headers.get("host"),
    secFetchSite: request.headers.get("sec-fetch-site"),
  });
  return allowed ? null : errorResponse({ code: ERROR_CODES.crossOrigin });
}

/** Reads a JSON request body; null when it is missing or not an object. */
export async function readRequestJson(request) {
  try {
    const data = await request.json();
    return data && typeof data === "object" && !Array.isArray(data) ? data : null;
  } catch {
    return null;
  }
}

function hostnameOf(request) {
  return new URL(request.url).hostname;
}

export async function getSessionToken() {
  return (await cookies()).get(SESSION_COOKIE)?.value || null;
}

/** @returns {Promise<{ kind: string, token: string } | null>} */
export async function getPendingToken() {
  return decodePending((await cookies()).get(PENDING_COOKIE)?.value);
}

/**
 * Stores the access token as the session and drops any pending MFA token.
 * @param {Request} request
 * @param {{ accessToken: string, expiresAt?: string }} token
 */
export async function setSession(request, { accessToken, expiresAt }) {
  const store = await cookies();
  store.set(
    SESSION_COOKIE,
    accessToken,
    cookieOptions({ hostname: hostnameOf(request), expiresAt, capSeconds: MAX_SESSION_SECONDS })
  );
  store.delete(PENDING_COOKIE);
}

/**
 * Stores an MFA challenge or setup token and drops any old session.
 * @param {Request} request
 * @param {string} kind  PENDING_KINDS.challenge or PENDING_KINDS.setup
 * @param {{ mfaToken: string, expiresAt?: string }} token
 */
export async function setPending(request, kind, { mfaToken, expiresAt }) {
  const store = await cookies();
  store.set(
    PENDING_COOKIE,
    encodePending(kind, mfaToken),
    cookieOptions({ hostname: hostnameOf(request), expiresAt, capSeconds: MAX_PENDING_SECONDS })
  );
  store.delete(SESSION_COOKIE);
}

export async function clearSession() {
  (await cookies()).delete(SESSION_COOKIE);
}

export async function clearAll() {
  const store = await cookies();
  store.delete(SESSION_COOKIE);
  store.delete(PENDING_COOKIE);
}

/** The narrow user the frontend needs, from a backend /auth/me body. */
export function toUser(me) {
  return {
    email: me.email,
    name: me.name,
    role: me.role,
    mustChangePassword: Boolean(me.mustChangePassword),
  };
}

/**
 * Loads the signed in user with a given access token.
 * @returns {Promise<{ ok: true, user: ReturnType<typeof toUser> } | { ok: false, result: any }>}
 */
export async function fetchMe(token) {
  const result = await callBackend("/auth/me", { token });
  if (!result.ok || !result.data) return { ok: false, result };
  return { ok: true, user: toUser(result.data) };
}

/**
 * The current user for Server Components. Read-only (Server Components
 * cannot change cookies), so an expired session is reported, not cleared.
 * @returns {Promise<{ status: "anonymous" } | { status: "expired" }
 *   | { status: "unavailable" } | { status: "signedIn", user: ReturnType<typeof toUser> }>}
 */
export async function getCurrentUser() {
  const token = await getSessionToken();
  if (!token) return { status: "anonymous" };

  const me = await fetchMe(token);
  if (me.ok) return { status: "signedIn", user: me.user };
  if (me.result.status === 401) return { status: "expired" };
  return { status: "unavailable" };
}
