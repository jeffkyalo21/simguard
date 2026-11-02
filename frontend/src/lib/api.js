import { copy } from "@/lib/copy";

// Browser calls only work once the backend enables CORS (#20).
export const API_URL = (
  process.env.NEXT_PUBLIC_API_URL || "http://localhost:5275"
).replace(/\/+$/, "");

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
  const url = new URL(`${API_URL}/api${path.startsWith("/") ? path : `/${path}`}`);

  for (const [key, value] of Object.entries(query ?? {})) {
    if (value !== undefined && value !== null && value !== "") {
      url.searchParams.set(key, String(value));
    }
  }

  return url;
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
  if (error.status === 404) return copy.errors.notFound;
  if (error.status === 409) return copy.errors.conflict;
  if (error.status >= 500) return copy.errors.server;

  return copy.errors.generic;
}
