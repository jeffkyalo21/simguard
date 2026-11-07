import { NextResponse } from "next/server";
import { PENDING_COOKIE, PENDING_KINDS, SESSION_COOKIE, decodePending, safeNextPath } from "@/lib/auth/core";
import { isPossibleSimId } from "@/lib/data";

// Runs before rendering. Two jobs:
//
// 1. A quick, optimistic sign-in check (cookie present or not). Pages in
//    the (app) group without a session cookie go to /login?next=<path>; the
//    MFA steps without their pending cookie go back to /login. The real
//    check (the backend accepting the token) happens in the (app) layout
//    and in every route handler, never here alone.
// 2. /sims/[id] is partially prerendered: its static shell is sent before
//    the page runs, so notFound() there can only stream a 404 page under a
//    200. Checking the id here, before rendering, gives unknown SIMs a real
//    404.

const PENDING_PAGES = {
  "/login/mfa": PENDING_KINDS.challenge,
  "/login/mfa-setup": PENDING_KINDS.setup,
};

function toLogin(request, params) {
  const url = new URL("/login", request.url);
  for (const [key, value] of Object.entries(params)) {
    if (value) url.searchParams.set(key, value);
  }
  return NextResponse.redirect(url);
}

export function proxy(request) {
  const { pathname, search } = request.nextUrl;
  const next = request.nextUrl.searchParams.get("next");

  if (pathname in PENDING_PAGES) {
    const pending = decodePending(request.cookies.get(PENDING_COOKIE)?.value);
    if (pending?.kind === PENDING_PAGES[pathname]) return NextResponse.next();
    return toLogin(request, { next: safeNextPath(next), reason: "restart" });
  }

  const hasSession = Boolean(request.cookies.get(SESSION_COOKIE)?.value);

  if (pathname === "/login/change-password") {
    return hasSession ? NextResponse.next() : toLogin(request, { next: safeNextPath(next) });
  }

  if (!hasSession) return toLogin(request, { next: safeNextPath(`${pathname}${search}`) });

  const parts = pathname.split("/");
  if (parts[1] === "sims" && parts.length === 3 && !isPossibleSimId(parts[2])) {
    return NextResponse.rewrite(new URL("/_not-found", request.url), { status: 404 });
  }

  return NextResponse.next();
}

export const config = {
  matcher: [
    "/dashboard/:path*",
    "/sims/:path*",
    "/alerts/:path*",
    "/usage/:path*",
    "/allocations/:path*",
    "/audit-log/:path*",
    "/settings/:path*",
    "/admin/:path*",
    "/imports/:path*",
    "/tier-recommendations/:path*",
    "/login/mfa",
    "/login/mfa-setup",
    "/login/change-password",
  ],
};
