"use client";

import { useState } from "react";
import Link from "next/link";
import { BarList } from "@/components/data/bar-list";
import { DataState } from "@/components/data/data-state";
import { DataTable } from "@/components/data/data-table";
import { FilterSelect } from "@/components/data/filter-select";
import { KpiCard } from "@/components/data/kpi-card";
import { LineChart } from "@/components/data/line-chart";
import { RiskBar } from "@/components/data/risk-bar";
import { SectionCard } from "@/components/data/section-card";
import { ChartSkeleton, ListSkeleton } from "@/components/data/skeletons";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/shared/error-state";
import { Button } from "@/components/ui/button";
import { useResource } from "@/hooks/use-resource";
import { getDashboard, referenceData } from "@/lib/data";
import { formatKes, formatNumber, formatPhone, formatShortDate } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.dashboard;

const periodOptions = referenceData.periods.map((days) => ({
  value: String(days),
  label: text.periods[days],
}));

const reviewColumns = [
  { key: "sim", header: copy.columns.sim, link: true, cell: (row) => formatPhone(row.phoneNumber) },
  { key: "employee", header: copy.columns.employee, cell: (row) => row.employeeName ?? copy.sims.unassigned },
  { key: "risk", header: copy.columns.risk, cell: (row) => <RiskBar score={row.riskScore} /> },
  { key: "type", header: copy.columns.type, cell: (row) => copy.alertTypes[row.type] },
  { key: "date", header: copy.columns.date, cell: (row) => formatShortDate(row.createdAt) },
];

function Kpis({ data }) {
  const kpis = data?.kpis;

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <KpiCard label={text.kpis.activeSims} value={formatNumber(kpis?.activeSims)} loading={!kpis} />
      <KpiCard label={text.kpis.flagged} value={formatNumber(kpis?.flagged)} loading={!kpis} hero />
      <KpiCard label={text.kpis.toReview} value={formatNumber(kpis?.toReview)} loading={!kpis} />
      <KpiCard label={text.kpis.savings} value={formatKes(kpis?.estSavingsKes)} loading={!kpis} />
    </div>
  );
}

function RiskChart({ series }) {
  const peak = series.reduce((max, point) => (point.risk > max.risk ? point : max), series[0]);

  return (
    <LineChart
      data={series}
      xKey="date"
      yKey="risk"
      label={text.riskOverTime}
      xTicks={[series[0].date, series.at(-1).date]}
      formatX={formatShortDate}
      yDomain={[0, 100]}
      peak={{ x: peak.date, y: peak.risk, label: text.peak }}
      height={240}
    />
  );
}

function TierChanges({ tiers }) {
  const rows = [
    { key: "upgrade", label: text.tierUpgrade, value: tiers.upgrade },
    { key: "downgrade", label: text.tierDowngrade, value: tiers.downgrade },
    { key: "keep", label: text.tierKeep, value: tiers.keep },
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

      {resource.error ? (
        <GlassCard>
          <ErrorState onRetry={resource.retry} className="py-6" />
        </GlassCard>
      ) : (
        <Kpis data={data} />
      )}

      <div className="grid gap-4 xl:grid-cols-3">
        <SectionCard title={text.riskOverTime} className="xl:col-span-2">
          <DataState
            resource={resource}
            skeleton={<ChartSkeleton height={240} />}
            isEmpty={(d) => !d.riskSeries.length}
            emptyMessage={text.emptyChart}
          >
            {(d) => <RiskChart series={d.riskSeries} />}
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
                  label: copy.alertTypes[type],
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
          strong
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

        <SectionCard title={text.tierChanges}>
          <DataState
            resource={resource}
            skeleton={<ListSkeleton rows={3} />}
            isEmpty={(d) => d.tierChanges.keep + d.tierChanges.upgrade + d.tierChanges.downgrade === 0}
            emptyMessage={text.emptyTiers}
          >
            {(d) => <TierChanges tiers={d.tierChanges} />}
          </DataState>
        </SectionCard>
      </div>
    </>
  );
}
