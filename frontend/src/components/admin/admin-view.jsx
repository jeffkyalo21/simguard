"use client";

import { useState } from "react";
import { useSearchParams } from "next/navigation";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { RequestsPanel } from "@/components/admin/requests-panel";
import { SystemHealth } from "@/components/admin/system-health";
import { UsersPanel } from "@/components/admin/users-panel";
import { PageHeader } from "@/components/layout/page-header";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { copy } from "@/lib/copy";

const text = copy.admin;
const TAB_KEYS = Object.keys(text.tabs);

export function AdminView() {
  // ?tab= opens a tab directly (the top bar bell links to ?tab=requests).
  const requested = useSearchParams().get("tab");
  const [tab, setTab] = useState(TAB_KEYS.includes(requested) ? requested : "health");
  const [seenRequest, setSeenRequest] = useState(requested);
  if (requested !== seenRequest) {
    setSeenRequest(requested);
    if (TAB_KEYS.includes(requested)) setTab(requested);
  }

  return (
    <>
      <PageHeader title={text.title} actions={<SignOutButton variant="outline" className="h-10 px-4" />} />
      <Tabs value={tab} onValueChange={setTab} className="gap-4">
        <TabsList variant="pill" aria-label={text.tabsLabel}>
          {Object.entries(text.tabs).map(([value, label]) => (
            <TabsTrigger key={value} value={value}>
              {label}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="health" className="flex flex-col gap-6">
          <p className="max-w-2xl text-sm text-muted-foreground">{text.intro}</p>
          <SystemHealth onOpenRequests={() => setTab("requests")} />
        </TabsContent>
        <TabsContent value="requests" className="flex flex-col gap-6">
          <RequestsPanel />
        </TabsContent>
        <TabsContent value="users" className="flex flex-col gap-6">
          <UsersPanel />
        </TabsContent>
      </Tabs>
    </>
  );
}
