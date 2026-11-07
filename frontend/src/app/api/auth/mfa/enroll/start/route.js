import { ERROR_CODES, PENDING_KINDS } from "@/lib/auth/core";
import {
  backendErrorResponse,
  callBackend,
  clearAll,
  errorResponse,
  getPendingToken,
  json,
  rejectCrossOrigin,
} from "@/lib/auth/server";

// First sign-in: creates a new authenticator key. Each call replaces the
// previous key, so the setup screen calls it once.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  const pending = await getPendingToken();
  if (pending?.kind !== PENDING_KINDS.setup) {
    await clearAll();
    return errorResponse({ code: ERROR_CODES.challengeExpired });
  }

  const result = await callBackend("/auth/mfa/enroll/start", { method: "POST", token: pending.token });
  if (!result.ok) {
    if (result.status === 401) await clearAll();
    return backendErrorResponse(result, "enroll");
  }

  const { sharedKey, authenticatorUri } = result.data ?? {};
  if (!sharedKey || !authenticatorUri) return errorResponse({ code: ERROR_CODES.server });
  return json({ sharedKey, authenticatorUri });
}
