"use client";

import { useState } from "react";
import { DataState } from "@/components/data/data-state";
import { DataTable } from "@/components/data/data-table";
import { LineChart } from "@/components/data/line-chart";
import { ProgressBar } from "@/components/data/progress-bar";
import { RiskBar } from "@/components/data/risk-bar";
import { SectionCard } from "@/components/data/section-card";
import { ChartSkeleton, ListSkeleton } from "@/components/data/skeletons";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { ErrorState } from "@/components/shared/error-state";
import { NotFoundContent } from "@/components/shared/not-found-content";
import { PageBreadcrumb } from "@/components/shared/page-breadcrumb";
import { RiskBadge } from "@/components/shared/risk-badge";
import { StatusBadge } from "@/components/shared/status-badge";
import { activityText } from "@/components/sims/activity-text";
import { SimStatusDialog, canChangeStatus } from "@/components/sims/sim-status-dialog";
import { TierDialog } from "@/components/sims/tier-dialog";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useResource } from "@/hooks/use-resource";
import { ApiError } from "@/lib/api";
import { getSim, getSimUsage, listSimActivity, listSimAlerts, listSimAllocations } from "@/lib/data";
import { formatDate, formatDateTime, formatPhone, formatShortDate } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.simProfile;

const alertColumns = [
  { key: "risk", header: copy.columns.risk, link: true, cell: (a) => <RiskBar score={a.riskScore} /> },
  { key: "type", header: copy.columns.type, cell: (a) => copy.alertTypes[a.type] ?? a.type },
  { key: "date", header: copy.columns.detected, cell: (a) => formatShortDate(a.detectedOn ?? a.createdAt) },
  { key: "status", header: copy.columns.status, cell: (a) => <RiskBadge level={a.status} /> },
];

const allocationColumns = [
  { key: "employee", header: copy.columns.employee, cell: (a) => a.employeeName },
  { key: "department", header: copy.columns.department, cell: (a) => a.departmentName },
  { key: "from", header: copy.columns.from, cell: (a) => formatDate(a.assignmentDate) },
  { key: "to", header: copy.columns.to, cell: (a) => (a.returnDate ? formatDate(a.returnDate) : text.current) },
];

const activityColumns = [
  { key: "time", header: copy.columns.time, cell: (e) => formatDateTime(e.occurredAt) },
  { key: "action", header: copy.columns.action, cell: activityText },
];

function Details({ sim }) {
  const allocation = sim.currentAllocation;
  const rows = [
    [text.employee, allocation?.employeeName ?? copy.sims.unassigned],
    [text.department, allocation?.departmentName ?? copy.common.noValue],
    [text.tier, copy.tiers[sim.tier ?? "none"] ?? sim.tier],
    [text.operator, sim.operator ?? copy.common.noValue],
    [text.iccid, sim.iccid],
    [text.assigned, allocation ? formatDate(allocation.assignedAt) : copy.common.noValue],
  ];

  return (
    <dl className="flex flex-col">
      {rows.map(([label, value]) => (
        <div key={label} className="flex items-center justify-between gap-4 border-b border-border py-3 text-sm last:border-0">
          <dt className="text-muted-foreground">{label}</dt>
          <dd className="text-right font-medium">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

function Usage({ usage }) {
  const hasLimit = usage.data.limit !== null;
  const top = Math.max(Math.ceil(Math.max(usage.data.limit ?? 0, usage.data.used) * 1.15), 1);
  const balances = [
    { key: "data", label: text.data, ...usage.data, valueText: hasLimit ? copy.units.ofGb(usage.data.used, usage.data.limit) : copy.units.gb(usage.data.used) },
    { key: "voice", label: text.voice, ...usage.voice, valueText: hasLimit ? copy.units.ofMin(usage.voice.used, usage.voice.limit) : usage.voice.used },
    { key: "sms", label: text.sms, ...usage.sms, valueText: hasLimit ? copy.units.ofCount(usage.sms.used, usage.sms.limit) : usage.sms.used },
  ];

  return (
    <div className="flex flex-col gap-5">
      <p className="text-sm text-muted-foreground">
        {text.cycle(formatDate(usage.cycleStart), formatDate(usage.cycleEnd), formatDate(usage.asOf))}
        {!hasLimit && ` ${text.noAllowance}`}
      </p>
      <LineChart
        data={usage.series}
        xKey="day"
        yKey="gb"
        xType="number"
        xDomain={[1, usage.cycleLength]}
        xTicks={[1, usage.cycleLength]}
        label={text.dataThisCycle}
        formatX={copy.units.day}
        formatY={copy.units.gb}
        yDomain={[0, top]}
        limit={hasLimit ? { y: usage.data.limit, label: text.limit } : undefined}
        height={200}
      />
      <div className="grid gap-4 sm:grid-cols-3">
        {balances.map(({ key, label, used, limit, valueText }) => (
          <div key={key} className="flex flex-col gap-2">
            <p className="text-sm text-muted-foreground">{label}</p>
            {limit !== null && <ProgressBar value={used} max={limit} label={label} valueText={valueText} />}
            <p className="text-sm tabular-nums">{valueText}</p>
          </div>
        ))}
      </div>
    </div>
  );
}

function RecordTable({ resource, columns, caption, emptyMessage, getRowHref }) {
  return (
    <GlassCard variant="strong" className="overflow-hidden p-0">
      <DataTable
        caption={caption}
        columns={columns}
        rows={resource.data?.items ?? null}
        getRowKey={(row) => row.id}
        getRowHref={getRowHref}
        error={resource.error}
        onRetry={resource.retry}
        emptyMessage={emptyMessage}
        skeletonRows={3}
      />
    </GlassCard>
  );
}

function ProfileSkeleton() {
  return (
    <div role="status" aria-busy="true" className="flex flex-col gap-6">
      <h1 className="sr-only">{copy.pages.simDetail.title}</h1>
      <span className="sr-only">{copy.a11y.loading}</span>
      <Skeleton className="h-5 w-48" />
      <Skeleton className="h-8 w-72" />
      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <GlassCard><ListSkeleton rows={6} /></GlassCard>
        <GlassCard><ChartSkeleton height={260} /></GlassCard>
      </div>
    </div>
  );
}

/** @param {{ id: number }} props */
export function SimProfile({ id }) {
  const simResource = useResource(() => getSim(id), [id]);
  const usage = useResource(() => getSimUsage(id), [id]);
  const alerts = useResource(() => listSimAlerts(id), [id]);
  const allocations = useResource(() => listSimAllocations(id), [id]);
  const activity = useResource(() => listSimActivity(id), [id]);

  // Dialogs keep the SIM they opened with, so nothing changes while closing.
  const [tierDialog, setTierDialog] = useState({ open: false, sim: null });
  const [statusDialog, setStatusDialog] = useState({ open: false, sim: null });

  const sim = simResource.data;

  if (simResource.error instanceof ApiError && simResource.error.status === 404) {
    return <NotFoundContent title={text.notFound} href="/sims" action={copy.actions.viewSims} />;
  }

  if (simResource.error) {
    return (
      <>
        <h1 className="sr-only">{copy.pages.simDetail.title}</h1>
        <ErrorState error={simResource.error} onRetry={simResource.retry} />
      </>
    );
  }
  if (!sim) return <ProfileSkeleton />;

  const number = formatPhone(sim.phoneNumber);
  const blocked = !canChangeStatus(sim);
  const statusLabel = sim.status === "inactive" ? text.activate : text.suspend;

  return (
    <>
      <PageBreadcrumb items={[{ label: copy.pages.sims.title, href: "/sims" }, { label: number }]} />

      <PageHeader
        title={number}
        badge={<StatusBadge status={sim.status} />}
        actions={
          <>
            <Button variant="outline" className="touch-target h-10 px-4" onClick={() => setTierDialog({ open: true, sim })}>
              {text.changeTier}
            </Button>
            <DisabledReason reason={copy.sims.allocatedReason} disabled={blocked}>
              <Button
                variant="outline"
                className="touch-target h-10 px-4"
                disabled={blocked}
                onClick={() => setStatusDialog({ open: true, sim })}
              >
                {statusLabel}
              </Button>
            </DisabledReason>
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <SectionCard title={text.details}>
          <Details sim={sim} />
        </SectionCard>
        <SectionCard title={text.dataThisCycle}>
          <DataState resource={usage} skeleton={<ChartSkeleton height={260} />}>
            {(data) => <Usage usage={data} />}
          </DataState>
        </SectionCard>
      </div>

      <Tabs defaultValue="alerts" className="gap-4">
        <TabsList variant="pill" aria-label={text.tabs.label}>
          <TabsTrigger value="alerts">{text.tabs.alerts}</TabsTrigger>
          <TabsTrigger value="allocations">{text.tabs.allocations}</TabsTrigger>
          <TabsTrigger value="activity">{text.tabs.activity}</TabsTrigger>
        </TabsList>
        <TabsContent value="alerts">
          <RecordTable
            resource={alerts}
            columns={alertColumns}
            caption={text.tabs.alerts}
            emptyMessage={text.noAlerts}
            getRowHref={(alert) => `/alerts/${alert.id}`}
          />
        </TabsContent>
        <TabsContent value="allocations">
          <RecordTable
            resource={allocations}
            columns={allocationColumns}
            caption={text.tabs.allocations}
            emptyMessage={text.noAllocations}
          />
        </TabsContent>
        <TabsContent value="activity">
          <RecordTable
            resource={activity}
            columns={activityColumns}
            caption={text.tabs.activity}
            emptyMessage={text.noActivity}
          />
        </TabsContent>
      </Tabs>

      <TierDialog
        sim={tierDialog.sim}
        open={tierDialog.open}
        onOpenChange={(open) => setTierDialog((current) => ({ ...current, open }))}
      />
      <SimStatusDialog
        sim={statusDialog.sim}
        open={statusDialog.open}
        onOpenChange={(open) => setStatusDialog((current) => ({ ...current, open }))}
      />
    </>
  );
}
