"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { Play } from "lucide-react";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { DataState } from "@/components/data/data-state";
import { DataTable } from "@/components/data/data-table";
import { SectionCard } from "@/components/data/section-card";
import { ListSkeleton } from "@/components/data/skeletons";
import { RequestsPanel } from "@/components/admin/requests-panel";
import { UsersPanel } from "@/components/admin/users-panel";
import { PageHeader } from "@/components/layout/page-header";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { Button } from "@/components/ui/button";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { usePermission } from "@/hooks/use-permission";
import { useResource } from "@/hooks/use-resource";
import { errorMessage } from "@/lib/api";
import { getScoringStatus, runScoring } from "@/lib/data";
import { bumpDataVersion } from "@/lib/data-version";
import { formatDate, formatDateTime, formatNumber } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.admin;
// While a run is queued or running the page checks again this often.
const POLL_MS = 3000;
const ACTIVE = new Set(["queued", "running"]);

function Fact({ label, children }) {
  return (
    <div className="flex flex-col gap-0.5">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="font-medium tabular-nums">{children}</dd>
    </div>
  );
}

function dateRange(run) {
  if (!run.firstDate) return copy.common.noValue;
  return run.firstDate === run.lastDate
    ? formatDate(run.firstDate)
    : `${formatDate(run.firstDate)} to ${formatDate(run.lastDate)}`;
}

const runColumns = [
  { key: "requested", header: text.columns.requested, cell: (run) => formatDateTime(run.requestedAt) },
  { key: "trigger", header: text.columns.trigger, cell: (run) => text.trigger[run.trigger] ?? run.trigger },
  { key: "status", header: text.columns.status, cell: (run) => text.runStatus[run.status] ?? run.status },
  { key: "dates", header: text.columns.dates, cell: dateRange },
  { key: "scored", header: text.columns.scored, className: "tabular-nums", cell: (run) => formatNumber(run.simDaysScored) },
  { key: "alerts", header: text.columns.alerts, cell: (run) => text.alertCounts(run.alertsCreated, run.alertsUpdated) },
];

export function AdminView() {
  const running = usePermission("scoring.run");
  const resource = useResource(() => getScoringStatus(), []);
  const [busy, setBusy] = useState(false);
  const [tab, setTab] = useState("scoring");
  const data = resource.data;
  const active = Boolean(data?.runs.some((run) => ACTIVE.has(run.status)));

  // Reload until the queued or running run has finished.
  useEffect(() => {
    if (!active) return undefined;
    const timer = setTimeout(bumpDataVersion, POLL_MS);
    return () => clearTimeout(timer);
  }, [active, data]);

  async function run() {
    setBusy(true);
    try {
      const result = await runScoring();
      toast.success(result.merged ? text.toasts.merged : text.toasts.started);
    } catch (error) {
      toast.error(errorMessage(error));
    } finally {
      setBusy(false);
    }
  }

  const ml = data?.mlService;
  const mlState = !ml ? null : !ml.configured ? text.mlNotConfigured : ml.reachable ? text.mlReachable : text.mlUnreachable;
  const buckets = data?.totals.latestScoredDateBuckets;

  return (
    <>
      <PageHeader
        title={text.title}
        actions={<SignOutButton variant="outline" className="h-10 px-4" />}
      />
      <Tabs value={tab} onValueChange={setTab} className="gap-4">
        <TabsList variant="pill" aria-label={text.tabsLabel}>
          {Object.entries(text.tabs).map(([value, label]) => (
            <TabsTrigger key={value} value={value}>
              {label}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="scoring" className="flex flex-col gap-6">
      <p className="max-w-2xl text-sm text-muted-foreground">{text.intro}</p>

      <SectionCard
        title={text.scoring}
        variant="strong"
        action={
          <DisabledReason reason={running.reason ?? ""} disabled={!running.allowed}>
            <Button className="h-10 px-4" disabled={!running.allowed || busy || !data} onClick={run}>
              <Play aria-hidden="true" />
              {text.run}
            </Button>
          </DisabledReason>
        }
      >
        <DataState resource={resource} skeleton={<ListSkeleton rows={3} />}>
          {(d) => (
            <dl className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
              <Fact label={d.enabled ? text.enabled : text.disabled}>
                {d.enabled ? text.dailyAt(d.dailyRunTimeNairobi) : copy.common.noValue}
                {d.enabled && d.nextScheduledRunAt && (
                  <span className="block text-sm font-normal text-muted-foreground">
                    {text.nextRun(formatDateTime(d.nextScheduledRunAt))}
                  </span>
                )}
              </Fact>
              <Fact label={text.ml}>
                {mlState}
                {ml.modelVersion && (
                  <span className="block text-sm font-normal text-muted-foreground">{text.modelVersion(ml.modelVersion)}</span>
                )}
              </Fact>
              <Fact label={text.riskScores}>{formatNumber(d.totals.riskScores)}</Fact>
              <Fact label={text.latestScored}>
                {formatDate(d.totals.latestScoredDate)}
                {buckets && (
                  <span className="block text-sm font-normal text-muted-foreground">
                    {text.latestBuckets(buckets.clear, buckets.escalated, buckets.flag)}
                  </span>
                )}
                <span className="block text-sm font-normal text-muted-foreground">
                  {`${text.latestUsage}: ${formatDate(d.totals.latestUsageDate)}`}
                </span>
              </Fact>
            </dl>
          )}
        </DataState>
      </SectionCard>

      <SectionCard title={text.runs} variant="strong" flush className="overflow-hidden">
        <DataTable
          caption={text.runs}
          columns={runColumns}
          rows={data?.runs ?? null}
          getRowKey={(row) => row.id}
          error={resource.error}
          onRetry={resource.retry}
          emptyMessage={text.noRuns}
        />
      </SectionCard>
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
