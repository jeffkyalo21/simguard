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

// Sets a new password from the emailed link. The token is only passed on to
// the backend: it is not logged here and the response never repeats it.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const body = await readRequestJson(request);
  const email = typeof body?.email === "string" ? body.email.trim() : "";
  const token = typeof body?.token === "string" ? body.token.trim() : "";
  const newPassword = typeof body?.newPassword === "string" ? body.newPassword : "";

  if (!email || !token) return errorResponse({ code: ERROR_CODES.invalidLink });
  if (!newPassword) return errorResponse({ code: ERROR_CODES.validation, fields: { newPassword: [] } });

  const result = await callBackend("/auth/reset-password", {
    method: "POST",
    body: { email, token, newPassword },
    headers: visitorAddressHeaders(request),
  });
  if (!result.ok) return backendErrorResponse(result, "reset");

  return json({ done: true });
}
