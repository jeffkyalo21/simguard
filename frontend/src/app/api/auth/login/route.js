import { ERROR_CODES, PENDING_KINDS } from "@/lib/auth/core";
import {
  backendErrorResponse,
  callBackend,
  errorResponse,
  json,
  readRequestJson,
  rejectCrossOrigin,
  setPending,
  visitorAddressHeaders,
} from "@/lib/auth/server";

// Step 1 of sign-in: email and password. The MFA challenge or setup token
// goes into the short-lived pending cookie; the browser only learns which
// screen comes next.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const body = await readRequestJson(request);
  const email = typeof body?.email === "string" ? body.email.trim() : "";
  const password = typeof body?.password === "string" ? body.password : "";

  if (!email || !password) {
    return errorResponse({ code: ERROR_CODES.validation, fields: {} });
  }

  const result = await callBackend("/auth/login", {
    method: "POST",
    body: { email, password },
    headers: visitorAddressHeaders(request),
  });
  if (!result.ok) return backendErrorResponse(result, "login");

  const { status, mfaToken, expiresAt } = result.data ?? {};
  if (!mfaToken) return errorResponse({ code: ERROR_CODES.server });

  if (status === "mfaEnrollmentRequired") {
    await setPending(request, PENDING_KINDS.setup, { mfaToken, expiresAt });
    return json({ next: "mfa-setup" });
  }

  await setPending(request, PENDING_KINDS.challenge, { mfaToken, expiresAt });
  return json({ next: "mfa" });
}
