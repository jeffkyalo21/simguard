import { AppSidebar } from "@/components/layout/app-sidebar";
import { Topbar } from "@/components/layout/topbar";
import { MockStoreProvider } from "@/lib/mock/store";

// The shell stays mounted across navigation; only the content area
// transitions (see template.jsx). The glass theme comes from <body>.
export default function AppLayout({ children }) {
  return (
    // TEMPORARY (#17): mock data store, replaced by the API in #20.
    <MockStoreProvider>
      <div className="flex min-h-dvh">
        <AppSidebar />
        <div className="flex min-w-0 flex-1 flex-col">
          <Topbar />
          <main id="main" tabIndex={-1} className="flex flex-1 flex-col gap-6 p-4 sm:p-8">
            {children}
          </main>
        </div>
      </div>
    </MockStoreProvider>
  );
}
