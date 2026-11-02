import { NextResponse } from "next/server";
import { isPossibleSimId } from "@/lib/data";

// /sims/[id] is partially prerendered: its static shell is sent before the
// page runs, so notFound() there can only stream a 404 page under a 200.
// Checking the id here, before rendering, gives unknown SIMs a real 404.
export function proxy(request) {
  const id = request.nextUrl.pathname.split("/")[2];
  if (isPossibleSimId(id)) return NextResponse.next();
  return NextResponse.rewrite(new URL("/_not-found", request.url), { status: 404 });
}

export const config = {
  matcher: "/sims/:id",
};
