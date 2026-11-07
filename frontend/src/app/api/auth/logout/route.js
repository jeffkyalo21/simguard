import { clearAll, rejectCrossOrigin } from "@/lib/auth/server";

// The backend has no logout endpoint (tokens are not revocable yet), so
// signing out drops both cookies.
export async function POST(request) {
  const blocked = rejectCrossOrigin(request);
  if (blocked) return blocked;

  await clearAll();
  return new Response(null, { status: 204, headers: { "Cache-Control": "no-store" } });
}
