"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { CarriersPanel } from "@/components/settings/carriers-panel";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { copy } from "@/lib/copy";

const text = copy.settings;
const TABS = ["carriers"];

// The selected tab lives in ?tab=, so a link can open one directly.
function writeTab(pathname, tab) {
  const params = new URLSearchParams(window.location.search);
  params.set("tab", tab);
  window.history.replaceState(null, "", `${pathname}?${params.toString()}`);
}

export function SettingsView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const requested = searchParams.get("tab");
  const tab = TABS.includes(requested) ? requested : "carriers";

  return (
    <>
      <PageHeader title={copy.pages.settings.title} />
      <Tabs value={tab} onValueChange={(next) => writeTab(pathname, next)} className="gap-4">
        <TabsList variant="pill" aria-label={text.tabsLabel}>
          {TABS.map((value) => (
            <TabsTrigger key={value} value={value}>
              {text.tabs[value]}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="carriers">
          <CarriersPanel />
        </TabsContent>
      </Tabs>
    </>
  );
}
