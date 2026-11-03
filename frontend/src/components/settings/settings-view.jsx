"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { Clock } from "lucide-react";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { CarriersPanel } from "@/components/settings/carriers-panel";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { copy } from "@/lib/copy";

const text = copy.settings;
const TABS = ["carriers", "departments", "plans", "profile"];
const SOON = ["departments", "plans", "profile"];

// The selected tab lives in ?tab=, so a link can open one directly.
function writeTab(pathname, tab) {
  const params = new URLSearchParams(window.location.search);
  params.set("tab", tab);
  window.history.replaceState(null, "", `${pathname}?${params.toString()}`);
}

function ComingSoon({ tab }) {
  return (
    <GlassCard variant="strong" className="flex flex-wrap items-center gap-3 p-5">
      <span className="inline-flex h-6 items-center gap-1 rounded-full border border-dashed border-input px-2.5 text-xs font-medium whitespace-nowrap text-muted-foreground">
        <Clock className="size-3.5" aria-hidden="true" />
        {text.comingSoon}
      </span>
      <p className="text-sm text-muted-foreground">{text.soon[tab]}</p>
    </GlassCard>
  );
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
              {SOON.includes(value) && <span className="sr-only">{`, ${text.comingSoon}`}</span>}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="carriers">
          <CarriersPanel />
        </TabsContent>
        {SOON.map((value) => (
          <TabsContent key={value} value={value}>
            <ComingSoon tab={value} />
          </TabsContent>
        ))}
      </Tabs>
    </>
  );
}
