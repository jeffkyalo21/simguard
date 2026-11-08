import { ERROR_CODES } from "@/lib/auth/core";
import {
  backendErrorResponse,
  callBackend,
  clearSession,
  errorResponse,
  fetchMe,
  getSessionToken,
  json,
  readRequestJson,
  rejectCrossOrigin,
  setSession,
} from "@/lib/auth/server";

// Works with a full session, also while the forced change is pending. The
// backend issues a new token (the old one stops working), which replaces
// the session.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const token = await getSessionToken();
  if (!token) return errorResponse({ code: ERROR_CODES.unauthorized });

  const body = await readRequestJson(request);
  const currentPassword = typeof body?.currentPassword === "string" ? body.currentPassword : "";
  const newPassword = typeof body?.newPassword === "string" ? body.newPassword : "";

  const result = await callBackend("/auth/change-password", {
    method: "POST",
    token,
    body: { currentPassword, newPassword },
  });
  if (!result.ok) {
    if (result.status === 401) await clearSession();
    return backendErrorResponse(result, "session");
  }

  const { accessToken, expiresAt } = result.data ?? {};
  if (!accessToken) return errorResponse({ code: ERROR_CODES.server });
  await setSession(request, { accessToken, expiresAt });

  const me = await fetchMe(accessToken);
  if (!me.ok) return backendErrorResponse(me.result, "session");
  return json({ role: me.user.role });
}
