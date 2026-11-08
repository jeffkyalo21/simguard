/**
 * Sends the landing page "Register" form to POST /api/access-requests
 * through the site's own route (the visitor has no session, so the call does
 * not go through /api/backend).
 * @param {{ name: string, email: string, organisation: string, role: string, note: string }} values
 * @returns {Promise<{ ok: true } | { ok: false, fields?: Record<string, unknown>,
 *   rateLimited?: boolean, network?: boolean }>}
 */
export async function requestAccess(values) {
  let response;

  try {
    response = await fetch("/api/access-requests", {
      method: "POST",
      credentials: "same-origin",
      cache: "no-store",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(values),
    });
  } catch {
    return { ok: false, network: true };
  }

  if (response.ok) return { ok: true };

  let body = null;
  try {
    body = await response.json();
  } catch {
    // Not JSON: treated as a generic failure below.
  }

  if (response.status === 429) return { ok: false, rateLimited: true };
  if (response.status === 400) return { ok: false, fields: body?.error?.fields ?? {} };
  if (response.status === 502) return { ok: false, network: true };
  return { ok: false };
}
