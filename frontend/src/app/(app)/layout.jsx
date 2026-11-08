import { redirect } from "next/navigation";
import { AuthProvider } from "@/components/auth/auth-provider";
import { RouteGuard } from "@/components/auth/route-guard";
import { PillNav } from "@/components/layout/pill-nav";
import { getCurrentUser } from "@/lib/auth/server";

// Every page in this group needs a signed in user, checked against the
// backend (/auth/me) on each request. The proxy already sent anyone without
// a session cookie to /login?next=...; here a rejected token goes to
// /login?reason=expired. The check blocks rather than streams: a Suspense
// boundary here would fix the status of /sims/[id] at 200 (see that page).
export const instant = false;

export default async function AppLayout({ children }) {
  const session = await getCurrentUser();

  if (session.status === "anonymous") redirect("/login");
  if (session.status === "expired") redirect("/login?reason=expired");
  if (session.status !== "signedIn") throw new Error("The session could not be checked.");
  if (session.user.mustChangePassword) redirect("/login/change-password");

  const { email, name, role } = session.user;

  // The pill nav stays mounted across navigation; only the content area
  // transitions (see template.jsx). The glass theme comes from <body>.
  return (
    <AuthProvider user={{ email, name, role }}>
      <div className="flex min-h-dvh flex-col">
        <PillNav />
        <main
          id="main"
          tabIndex={-1}
          className="mx-auto flex w-full max-w-[1280px] flex-1 flex-col gap-6 px-6 pt-10 pb-16"
        >
          <RouteGuard>{children}</RouteGuard>
        </main>
      </div>
    </AuthProvider>
  );
}
