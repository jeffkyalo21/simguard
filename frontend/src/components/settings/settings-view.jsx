"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { PageHeader } from "@/components/layout/page-header";
import { useAuth } from "@/components/auth/auth-provider";
import { CarriersPanel } from "@/components/settings/carriers-panel";
import { ProfilePanel } from "@/components/settings/profile-panel";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { canOpen } from "@/lib/permissions";
import { copy } from "@/lib/copy";

const text = copy.settings;
// Carriers is for the roles that read organisation data; Profile is for everyone.
const TABS = ["carriers", "profile"];
const TAB_PAGE = { carriers: "carriers", profile: "settings" };

// The selected tab lives in ?tab=, so a link can open one directly.
function writeTab(pathname, tab) {
  const params = new URLSearchParams(window.location.search);
  params.set("tab", tab);
  window.history.replaceState(null, "", `${pathname}?${params.toString()}`);
}

export function SettingsView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { role } = useAuth();
  const tabs = TABS.filter((value) => canOpen(role, TAB_PAGE[value]));
  const requested = searchParams.get("tab");
  const tab = tabs.includes(requested) ? requested : tabs[0];

  return (
    <>
      <PageHeader title={copy.pages.settings.title} />
      <Tabs value={tab} onValueChange={(next) => writeTab(pathname, next)} className="gap-4">
        <TabsList variant="pill" aria-label={text.tabsLabel}>
          {tabs.map((value) => (
            <TabsTrigger key={value} value={value}>
              {text.tabs[value]}
            </TabsTrigger>
          ))}
        </TabsList>
        {tabs.includes("carriers") && (
          <TabsContent value="carriers">
            <CarriersPanel />
          </TabsContent>
        )}
        <TabsContent value="profile">
          <ProfilePanel />
        </TabsContent>
      </Tabs>
    </>
  );
}
