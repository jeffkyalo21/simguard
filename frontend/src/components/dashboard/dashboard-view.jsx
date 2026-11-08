"use client";

import { useState } from "react";
import Link from "next/link";
import { BarList } from "@/components/data/bar-list";
import { DataState } from "@/components/data/data-state";
import { DataTable } from "@/components/data/data-table";
import { FilterSelect } from "@/components/data/filter-select";
import { KpiCard } from "@/components/data/kpi-card";
import { RiskBar } from "@/components/data/risk-bar";
import { SectionCard } from "@/components/data/section-card";
import { ListSkeleton } from "@/components/data/skeletons";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { SetupChecklist } from "@/components/dashboard/setup-checklist";
import { ErrorState } from "@/components/shared/error-state";
import { Button } from "@/components/ui/button";
import { useResource } from "@/hooks/use-resource";
import { getDashboard, referenceData } from "@/lib/data";
import { formatDate, formatNumber, formatPhone, formatShortDate } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.dashboard;

const formatGb = (value) => copy.units.gb(formatNumber(value));

const periodOptions = referenceData.periods.map((days) => ({
  value: String(days),
  label: text.periods[days],
}));

const reviewColumns = [
  { key: "sim", header: copy.columns.sim, link: true, cell: (row) => formatPhone(row.simPhoneNumber) },
  { key: "employee", header: copy.columns.employee, cell: (row) => row.employeeName ?? copy.sims.unassigned },
  { key: "risk", header: copy.columns.risk, cell: (row) => <RiskBar score={row.riskScore} /> },
  { key: "type", header: copy.columns.type, cell: (row) => copy.alertTypes[row.type] ?? row.type },
  { key: "date", header: copy.columns.detected, cell: (row) => formatShortDate(row.detectedOn ?? row.createdAt) },
];

function Kpis({ data }) {
  const kpis = data?.kpis;

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <KpiCard label={text.kpis.activeSims} value={formatNumber(kpis?.activeSims)} loading={!kpis} />
      <KpiCard label={text.kpis.flagged} value={formatNumber(kpis?.flagged)} loading={!kpis} hero />
      <KpiCard label={text.kpis.toReview} value={formatNumber(kpis?.toReview)} loading={!kpis} />
      <KpiCard label={text.kpis.resolved} value={formatNumber(kpis?.resolved)} loading={!kpis} />
    </div>
  );
}

/**
 * Unused paid data for the period: the total, used against paid, and the
 * departments wasting most. Screen readers get the numbers as one sentence;
 * the visual number and bar are hidden from them.
 */
function DataWaste({ waste }) {
  const { paidGb, usedGb, unusedGb, percent, departments } = waste;
  const usedShare = paidGb > 0 ? (usedGb / paidGb) * 100 : 0;

  return (
    <div className="flex flex-1 flex-col gap-6">
      <p className="sr-only">
        {`${text.wasteUnused(formatGb(unusedGb), formatGb(paidGb))} ${text.wasteUsed(formatGb(usedGb), percent)}`}
      </p>
      <div aria-hidden="true" className="flex flex-col gap-1">
        <p className="text-4xl font-semibold tracking-tight tabular-nums">{formatGb(unusedGb)}</p>
        <p className="text-sm text-muted-foreground">{text.wastedShare(percent)}</p>
      </div>
      <div aria-hidden="true" className="flex flex-col gap-2">
        <div className="h-2.5 w-full overflow-hidden rounded-full bg-chart-track">
          <div className="h-full rounded-full bg-foreground" style={{ width: `${usedShare}%` }} />
        </div>
        <div className="flex justify-between text-sm">
          <span>
            <span className="text-muted-foreground">{text.used}</span>{" "}
            <span className="font-medium tabular-nums">{formatGb(usedGb)}</span>
          </span>
          <span>
            <span className="text-muted-foreground">{text.paid}</span>{" "}
            <span className="font-medium tabular-nums">{formatGb(paidGb)}</span>
          </span>
        </div>
      </div>
      <div className="flex flex-col gap-3">
        <h3 className="text-sm font-medium text-muted-foreground">{text.topDepartments}</h3>
        <BarList
          items={departments.map(({ name, unusedGb: value }) => ({ key: name ?? "none", label: name ?? copy.sims.unassigned, value }))}
          formatValue={formatGb}
        />
      </div>
    </div>
  );
}

function TierChanges({ tiers }) {
  const rows = [
    { key: "upgrade", label: text.tierUpgrade, value: tiers.upgrade },
    { key: "downgrade", label: text.tierDowngrade, value: tiers.downgrade },
  ];

  return (
    <div className="flex flex-1 flex-col justify-between gap-6">
      <dl className="flex flex-col">
        {rows.map(({ key, label, value }) => (
          <div key={key} className="flex items-center justify-between border-b border-border py-3 text-sm">
            <dt>{label}</dt>
            <dd className="font-semibold tabular-nums">{formatNumber(value)}</dd>
          </div>
        ))}
      </dl>
      <Button asChild variant="outline" className="touch-target h-10 w-full">
        <Link href="/allocations">{copy.actions.review}</Link>
      </Button>
    </div>
  );
}

export function DashboardView() {
  const [days, setDays] = useState("30");
  const resource = useResource(() => getDashboard(Number(days)), [days]);
  const { data } = resource;

  return (
    <>
      <PageHeader
        title={copy.pages.dashboard.title}
        actions={
          <FilterSelect
            label={text.period}
            value={days}
            options={periodOptions}
            onChange={setDays}
            includeAll={false}
            className="min-w-36"
          />
        }
      />

      {data?.asOf && <p className="-mt-2 text-sm text-muted-foreground">{text.asOf(formatDate(data.asOf))}</p>}

      <SetupChecklist />

      {resource.error ? (
        <GlassCard>
          <ErrorState error={resource.error} onRetry={resource.retry} className="py-6" />
        </GlassCard>
      ) : (
        <Kpis data={data} />
      )}

      <div className="grid gap-4 xl:grid-cols-3">
        <SectionCard title={text.dataWasted} className="xl:col-span-2">
          <DataState
            resource={resource}
            skeleton={<ListSkeleton rows={4} />}
            isEmpty={(d) => d.dataWaste.paidGb === 0}
            emptyMessage={text.emptyWaste}
          >
            {(d) => <DataWaste waste={d.dataWaste} />}
          </DataState>
        </SectionCard>

        <SectionCard title={text.alertsByType}>
          <DataState
            resource={resource}
            skeleton={<ListSkeleton />}
            isEmpty={(d) => d.alertsByType.every((item) => item.count === 0)}
            emptyMessage={text.emptyAlerts}
          >
            {(d) => (
              <BarList
                items={d.alertsByType.map(({ type, count }) => ({
                  key: type,
                  label: copy.alertTypes[type] ?? type,
                  value: count,
                }))}
              />
            )}
          </DataState>
        </SectionCard>
      </div>

      <div className="grid gap-4 xl:grid-cols-3">
        <SectionCard
          title={text.needsReview}
          variant="dark"
          flush
          className="overflow-hidden xl:col-span-2"
          action={
            <Link href="/alerts" className="link-accent touch-target inline-flex h-10 items-center px-2 text-sm font-medium">
              {copy.actions.viewAll}
            </Link>
          }
        >
          <DataTable
            caption={text.needsReview}
            columns={reviewColumns}
            rows={data?.needsReview ?? null}
            getRowKey={(row) => row.id}
            getRowHref={(row) => `/alerts/${row.id}`}
            error={resource.error}
            onRetry={resource.retry}
            emptyMessage={text.emptyReview}
          />
        </SectionCard>

        <SectionCard
          title={text.tierChanges}
          action={
            <Link
              href="/tier-recommendations"
              className="link-accent touch-target inline-flex h-10 items-center px-2 text-sm font-medium"
            >
              {copy.actions.viewAll}
              <span className="sr-only">{` ${copy.tierRecommendations.title}`}</span>
            </Link>
          }
        >
          <DataState
            resource={resource}
            skeleton={<ListSkeleton rows={3} />}
            isEmpty={(d) => d.tierChanges.upgrade + d.tierChanges.downgrade === 0}
            emptyMessage={text.emptyTiers}
          >
            {(d) => <TierChanges tiers={d.tierChanges} />}
          </DataState>
        </SectionCard>
      </div>
    </>
  );
}
