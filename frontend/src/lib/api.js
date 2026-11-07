import { loginHref } from "@/lib/auth/client";
import { copy } from "@/lib/copy";

// Browser calls go to the forwarding route handler on this site, which adds
// the session token and passes the backend's status and ProblemDetails body
// through unchanged. The browser never holds the token.
export const API_BASE = "/api/backend";

/**
 * A 401 means the backend rejected the session (the route handler has
 * already cleared the cookie): send the person to sign in again.
 */
function onSessionExpired() {
  if (typeof window === "undefined") return;
  const next = `${window.location.pathname}${window.location.search}`;
  window.location.assign(loginHref({ next, reason: "expired" }));
}

/**
 * Paged list returned by every list endpoint.
 * @template T
 * @typedef {Object} PagedResult
 * @property {T[]} items
 * @property {number} page      1-based
 * @property {number} pageSize  default 20, max 100
 * @property {number} total
 */

/**
 * Error carrying an RFC 7807 ProblemDetails body.
 * `errors` maps field names to messages (400 validation responses).
 */
export class ApiError extends Error {
  /**
   * @param {{ status: number, title?: string, detail?: string,
   *   errors?: Record<string, string[]>, cause?: unknown }} problem
   */
  constructor({ status, title, detail, errors, cause }) {
    super(detail || title || `Request failed with status ${status}`, { cause });
    this.name = "ApiError";
    /** 0 when the request never reached the server. */
    this.status = status;
    this.title = title ?? "";
    this.detail = detail ?? "";
    this.errors = errors ?? {};
  }
}

function buildUrl(path, query) {
  const params = new URLSearchParams();

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null && value !== "") {
      params.set(key, String(value));
    }
  }

  const search = params.toString();
  return `${API_BASE}${path.startsWith("/") ? path : `/${path}`}${search ? `?${search}` : ""}`;
}

async function readBody(response) {
  if (response.status === 204) {
    return undefined;
  }

  const contentType = response.headers.get("content-type") ?? "";
  const text = await response.text();

  if (!text) {
    return undefined;
  }

  return contentType.includes("json") ? JSON.parse(text) : text;
}

/**
 * Sends a request to the SimGuard API. Paths are relative to /api.
 * @param {"GET"|"POST"|"PUT"|"PATCH"|"DELETE"} method
 * @param {string} path  for example "/sims" or "/sims/1/status"
 * @param {{ body?: unknown, query?: Record<string, unknown>,
 *   signal?: AbortSignal, headers?: Record<string, string> }} [options]
 */
export async function request(method, path, { body, query, signal, headers } = {}) {
  let response;

  try {
    response = await fetch(buildUrl(path, query), {
      method,
      signal,
      credentials: "same-origin",
      cache: "no-store",
      headers: {
        Accept: "application/json",
        ...(body !== undefined && { "Content-Type": "application/json" }),
        ...headers,
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  } catch (cause) {
    if (cause?.name === "AbortError") {
      throw cause;
    }

    throw new ApiError({ status: 0, title: "Network error", cause });
  }

  const data = await readBody(response);

  if (!response.ok) {
    const problem = typeof data === "object" && data !== null ? data : {};

    if (response.status === 401) onSessionExpired();

    // The route handler answers { error: { code: "network" } } when the
    // backend cannot be reached; report it like a failed fetch.
    if (problem.error?.code === "network") {
      throw new ApiError({ status: 0, title: "Network error" });
    }

    throw new ApiError({
      status: response.status,
      title: problem.title,
      detail: problem.detail ?? (typeof data === "string" ? data : undefined),
      errors: problem.errors,
    });
  }

  return data;
}

export const api = {
  get: (path, options) => request("GET", path, options),
  post: (path, body, options) => request("POST", path, { ...options, body }),
  put: (path, body, options) => request("PUT", path, { ...options, body }),
  patch: (path, body, options) => request("PATCH", path, { ...options, body }),
  delete: (path, options) => request("DELETE", path, options),
};

/** Short, user-facing message for an error. Never shows status codes. */
export function errorMessage(error) {
  if (!(error instanceof ApiError)) {
    return copy.errors.generic;
  }

  if (error.status === 0) return copy.errors.network;
  if (error.status === 400) return copy.errors.validation;
  if (error.status === 403) return copy.errors.forbidden;
  if (error.status === 404) return copy.errors.notFound;
  if (error.status === 409) return copy.errors.conflict;
  if (error.status >= 500) return copy.errors.server;

  return copy.errors.generic;
}

/**
 * Message for a read that failed: a clear line for a role that may not see
 * the data (403) and for an expired session (401, the sign-in page is
 * already loading), otherwise the generic load error.
 */
export function loadErrorMessage(error) {
  if (error instanceof ApiError) {
    if (error.status === 401) return copy.errors.sessionExpired;
    if (error.status === 403) return copy.errors.forbiddenRead;
    if (error.status === 404) return copy.errors.notFound;
    if (error.status === 0) return copy.errors.network;
  }

  return copy.errors.load;
}

/** True when trying the same read again cannot help (401 and 403). */
export function isFinalLoadError(error) {
  return error instanceof ApiError && (error.status === 401 || error.status === 403);
}
