import { PillNav } from "@/components/layout/pill-nav";
import { MockStoreProvider } from "@/lib/mock/store";

// The pill nav stays mounted across navigation; only the content area
// transitions (see template.jsx). The glass theme comes from <body>.
export default function AppLayout({ children }) {
  return (
    // TEMPORARY (#17): mock data store, replaced by the API in #20.
    <MockStoreProvider>
      <div className="flex min-h-dvh flex-col">
        <PillNav />
        <main
          id="main"
          tabIndex={-1}
          className="mx-auto flex w-full max-w-[1280px] flex-1 flex-col gap-6 px-6 pt-10 pb-16"
        >
          {children}
        </main>
      </div>
    </MockStoreProvider>
  );
}
