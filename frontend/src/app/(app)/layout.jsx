import { AppSidebar } from "@/components/layout/app-sidebar";
import { Topbar } from "@/components/layout/topbar";

// The only place .glass-app is applied: the glass theme cannot reach the
// landing or login pages.
export default function AppLayout({ children }) {
  return (
    <div className="glass-app relative isolate flex min-h-dvh">
      <div className="glass-backdrop" aria-hidden="true">
        <span className="glass-orb" />
      </div>
      <AppSidebar />
      <div className="flex min-w-0 flex-1 flex-col">
        <Topbar />
        <main id="main" tabIndex={-1} className="flex flex-1 flex-col gap-6 p-4 sm:p-8">
          {children}
        </main>
      </div>
    </div>
  );
}
