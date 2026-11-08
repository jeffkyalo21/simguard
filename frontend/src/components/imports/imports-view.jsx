"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { FilterSelect } from "@/components/data/filter-select";
import { PageHeader } from "@/components/layout/page-header";
import { HistoryPanel } from "@/components/imports/history-panel";
import { ImportPanel } from "@/components/imports/import-panel";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { IMPORT_TYPES } from "@/lib/data";
import { copy } from "@/lib/copy";

const text = copy.imports;
const TABS = ["import", "history"];

const typeOptions = IMPORT_TYPES.map((value) => ({ value, label: text.types[value] }));

// The type and the tab live in the URL, so /imports?type=sims opens ready.
function writeQuery(pathname, changes) {
  const params = new URLSearchParams(window.location.search);
  for (const [key, value] of Object.entries(changes)) params.set(key, value);
  window.history.replaceState(null, "", `${pathname}?${params.toString()}`);
}

export function ImportsView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const requestedTab = searchParams.get("tab");
  const requestedType = searchParams.get("type");
  const tab = TABS.includes(requestedTab) ? requestedTab : "import";
  const type = IMPORT_TYPES.includes(requestedType) ? requestedType : IMPORT_TYPES[0];

  return (
    <>
      <PageHeader title={copy.pages.imports.title} />
      <p className="-mt-2 max-w-2xl text-sm text-muted-foreground">{text.intro}</p>
      <Tabs value={tab} onValueChange={(next) => writeQuery(pathname, { tab: next })} className="gap-4">
        <TabsList variant="pill" aria-label={text.tabsLabel}>
          {TABS.map((value) => (
            <TabsTrigger key={value} value={value}>
              {text.tabs[value]}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="import" className="flex flex-col gap-4">
          <FilterSelect
            label={text.typeLabel}
            value={type}
            options={typeOptions}
            onChange={(next) => writeQuery(pathname, { type: next })}
            includeAll={false}
            className="min-w-48 self-start"
          />
          {/* Keyed by type: choosing another type drops the file and result. */}
          <ImportPanel key={type} type={type} />
        </TabsContent>
        <TabsContent value="history">
          <HistoryPanel />
        </TabsContent>
      </Tabs>
    </>
  );
}
