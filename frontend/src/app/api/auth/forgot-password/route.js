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

// Asks for a password reset email. The backend answers the same for every
// address, and so does this handler: the screen never learns whether the
// account exists.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const body = await readRequestJson(request);
  const email = typeof body?.email === "string" ? body.email.trim() : "";
  if (!email) return errorResponse({ code: ERROR_CODES.validation, fields: {} });

  const result = await callBackend("/auth/forgot-password", {
    method: "POST",
    body: { email },
    headers: visitorAddressHeaders(request),
  });
  if (!result.ok) return backendErrorResponse(result, "session");

  return json({ sent: true });
}
