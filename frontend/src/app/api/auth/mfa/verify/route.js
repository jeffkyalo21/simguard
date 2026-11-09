import { ERROR_CODES, PENDING_KINDS } from "@/lib/auth/core";
import {
  backendErrorResponse,
  callBackend,
  clearAll,
  errorResponse,
  fetchMe,
  getPendingToken,
  json,
  readRequestJson,
  rejectCrossOrigin,
  setSession,
  visitorAddressHeaders,
} from "@/lib/auth/server";

// Step 2 of a normal sign-in: a TOTP code or one recovery code.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const pending = await getPendingToken();
  if (pending?.kind !== PENDING_KINDS.challenge) {
    await clearAll();
    return errorResponse({ code: ERROR_CODES.challengeExpired });
  }

  const body = await readRequestJson(request);
  const code = typeof body?.code === "string" ? body.code.replace(/\s+/g, "") : "";
  if (!code) return errorResponse({ code: ERROR_CODES.validation, fields: {} });

  const result = await callBackend("/auth/mfa/verify", {
    method: "POST",
    body: { mfaToken: pending.token, code },
    headers: visitorAddressHeaders(request),
  });
  if (!result.ok) return backendErrorResponse(result, "mfa");

  const { accessToken, expiresAt } = result.data ?? {};
  if (!accessToken) return errorResponse({ code: ERROR_CODES.server });
  await setSession(request, { accessToken, expiresAt });

  const me = await fetchMe(accessToken);
  if (!me.ok) return backendErrorResponse(me.result, "session");
  return json({ role: me.user.role, mustChangePassword: me.user.mustChangePassword });
}
