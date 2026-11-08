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
} from "@/lib/auth/server";

// First sign-in: confirms the first code. The recovery codes are returned
// once, here, and never stored by the frontend.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const pending = await getPendingToken();
  if (pending?.kind !== PENDING_KINDS.setup) {
    await clearAll();
    return errorResponse({ code: ERROR_CODES.challengeExpired });
  }

  const body = await readRequestJson(request);
  const code = typeof body?.code === "string" ? body.code.replace(/\s+/g, "") : "";
  if (!code) return errorResponse({ code: ERROR_CODES.validation, fields: {} });

  const result = await callBackend("/auth/mfa/enroll/confirm", {
    method: "POST",
    token: pending.token,
    body: { code },
  });
  if (!result.ok) {
    if (result.status === 401) await clearAll();
    return backendErrorResponse(result, "enroll");
  }

  const { accessToken, expiresAt, recoveryCodes } = result.data ?? {};
  if (!accessToken) return errorResponse({ code: ERROR_CODES.server });
  await setSession(request, { accessToken, expiresAt });

  const me = await fetchMe(accessToken);
  if (!me.ok) return backendErrorResponse(me.result, "session");
  return json({
    recoveryCodes: Array.isArray(recoveryCodes) ? recoveryCodes : [],
    role: me.user.role,
    mustChangePassword: me.user.mustChangePassword,
  });
}
