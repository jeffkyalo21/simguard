import { ERROR_CODES } from "@/lib/auth/core";
import { backendErrorResponse, callBackend, errorResponse, json, readRequestJson, rejectCrossOrigin } from "@/lib/auth/server";

// The landing page "Register" form. Visitors have no session, so this is
// not part of the /api/backend forwarder (which needs a token). It checks
// the origin, passes the five fields on to POST /api/access-requests and
// hands the visitor's address on as X-Forwarded-For, so the API's rate limit
// counts visitors one by one and not this server as a whole.

const FIELDS = ["name", "email", "organisation", "role", "note"];

/**
 * The visitor's address: the last entry of X-Forwarded-For, which is the
 * one the hosting proxy in front of this server added. Null when the header
 * is missing (local development).
 */
function visitorAddress(request) {
  const chain = request.headers.get("x-forwarded-for");
  const last = chain?.split(",").at(-1)?.trim();
  return last || null;
}

export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const body = await readRequestJson(request);
  if (!body) return errorResponse({ code: ERROR_CODES.validation, fields: {} });

  const payload = {};
  for (const field of FIELDS) {
    payload[field] = typeof body[field] === "string" ? body[field] : "";
  }

  const address = visitorAddress(request);
  const result = await callBackend("/access-requests", {
    method: "POST",
    body: payload,
    ...(address && { headers: { "X-Forwarded-For": address } }),
  });

  if (!result.ok) return backendErrorResponse(result, "access-request");
  return json({ received: true }, 202);
}
