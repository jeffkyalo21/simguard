import { ERROR_CODES } from "@/lib/auth/core";
import {
  backendErrorResponse,
  callBackend,
  errorResponse,
  json,
  readRequestJson,
  rejectCrossOrigin,
  visitorAddressHeaders,
} from "@/lib/auth/server";

// The landing page "Register" form. Visitors have no session, so this is
// not part of the /api/backend forwarder (which needs a token). It checks
// the origin, passes the five fields on to POST /api/access-requests and
// hands the visitor's address on as X-Forwarded-For, so the API's rate limit
// counts visitors one by one and not this server as a whole.

const FIELDS = ["name", "email", "organisation", "role", "note"];

export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const body = await readRequestJson(request);
  if (!body) return errorResponse({ code: ERROR_CODES.validation, fields: {} });

  const payload = {};
  for (const field of FIELDS) {
    payload[field] = typeof body[field] === "string" ? body[field] : "";
  }

  const result = await callBackend("/access-requests", {
    method: "POST",
    body: payload,
    headers: visitorAddressHeaders(request),
  });

  if (!result.ok) return backendErrorResponse(result, "access-request");
  return json({ received: true }, 202);
}
