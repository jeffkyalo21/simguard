import { ERROR_CODES } from "@/lib/auth/core";
import {
  backendErrorResponse,
  clearSession,
  errorResponse,
  fetchMe,
  getSessionToken,
  json,
} from "@/lib/auth/server";

// The signed in user, without any token. A 401 from the backend ends the
// session here too.
export async function GET() {
  const token = await getSessionToken();
  if (!token) return errorResponse({ code: ERROR_CODES.unauthorized });

  const me = await fetchMe(token);
  if (!me.ok) {
    if (me.result.status === 401) await clearSession();
    return backendErrorResponse(me.result, "session");
  }

  return json(me.user);
}
