import { ERROR_CODES, forwardablePath } from "@/lib/auth/core";
import {
  backendUrl,
  clearSession,
  errorResponse,
  getSessionToken,
  rejectCrossOrigin,
} from "@/lib/auth/server";

// Forwards data calls to the backend: /api/backend/<path>?<query> goes to
// BACKEND_URL/api/<path>?<query> with the session token as a Bearer header.
// Status codes and ProblemDetails bodies come back unchanged. A backend 401
// ends the session. Auth endpoints (which return tokens) are not forwarded,
// except GET auth/events. Request bodies (JSON or a multipart CSV upload) are
// streamed through without buffering.

const TIMEOUT_MS = 30_000;
// CSV imports upload up to 25 MB and validate every row, so they get longer.
const IMPORT_TIMEOUT_MS = 300_000;
const REQUEST_HEADERS = ["accept", "content-type"];
const RESPONSE_HEADERS = ["content-type", "content-disposition", "location", "retry-after"];

async function forward(request, { params }) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const { path: segments } = await params;
  const path = forwardablePath(segments, request.method);
  if (!path) return errorResponse({ code: ERROR_CODES.notFound });

  const token = await getSessionToken();
  if (!token) return errorResponse({ code: ERROR_CODES.unauthorized });

  const headers = new Headers({ Authorization: `Bearer ${token}` });
  for (const name of REQUEST_HEADERS) {
    const value = request.headers.get(name);
    if (value) headers.set(name, value);
  }

  const hasBody = !["GET", "HEAD"].includes(request.method);
  const timeout = path.toLowerCase().startsWith("imports") ? IMPORT_TIMEOUT_MS : TIMEOUT_MS;
  let response;

  try {
    response = await fetch(`${backendUrl()}/api/${path}${request.nextUrl.search}`, {
      method: request.method,
      headers,
      // The request body is passed on as a stream, so a 25 MB upload is not
      // held in memory here. "half" duplex is required for a streamed body.
      body: hasBody ? request.body : undefined,
      ...(hasBody && { duplex: "half" }),
      cache: "no-store",
      redirect: "manual",
      signal: AbortSignal.timeout(timeout),
    });
  } catch {
    return errorResponse({ code: ERROR_CODES.network });
  }

  if (response.status === 401) await clearSession();

  const outHeaders = new Headers({ "Cache-Control": "no-store" });
  for (const name of RESPONSE_HEADERS) {
    const value = response.headers.get(name);
    if (value) outHeaders.set(name, value);
  }

  const body = [204, 304].includes(response.status) ? null : await response.arrayBuffer();
  return new Response(body, { status: response.status, headers: outHeaders });
}

export const GET = forward;
export const POST = forward;
export const PUT = forward;
export const PATCH = forward;
export const DELETE = forward;
