"use client";

import { useState } from "react";
import { BarList } from "@/components/data/bar-list";
import { DataState } from "@/components/data/data-state";
import { DataTable } from "@/components/data/data-table";
import { FilterSelect } from "@/components/data/filter-select";
import { KpiCard } from "@/components/data/kpi-card";
import { LineChart } from "@/components/data/line-chart";
import { SectionCard } from "@/components/data/section-card";
import { ChartSkeleton, ListSkeleton } from "@/components/data/skeletons";
import { PageHeader } from "@/components/layout/page-header";
import { ErrorState } from "@/components/shared/error-state";
import { useResource } from "@/hooks/use-resource";
import { getUsageAnalytics, referenceData } from "@/lib/data";
import { formatNumber, formatPhone, formatShortDate } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.usageAnalytics;
const periodOptions = referenceData.periods.map((days) => ({ value: String(days), label: copy.dashboard.periods[days] }));
const departmentOptions = referenceData.departments.map((name) => ({ value: name, label: name }));
const planOptions = referenceData.planTypes.map((name) => ({ value: name, label: name }));
const formatGb = (value) => `${formatNumber(Number(value.toFixed(1)))} GB`;

const simColumns = [
  { key: "sim", header: copy.columns.sim, link: true, cell: (row) => formatPhone(row.phoneNumber) },
  { key: "employee", header: copy.columns.employee, cell: (row) => row.employeeName ?? copy.sims.unassigned },
  { key: "department", header: copy.columns.department, cell: (row) => row.departmentName ?? copy.common.noValue },
  { key: "plan", header: text.plan, cell: (row) => row.planType },
  { key: "usage", header: text.dataUsed, cell: (row) => formatGb(row.dataUsedGb) },
  { key: "utilisation", header: text.utilisation, cell: (row) => copy.units.percent(row.utilisationPercent) },
];

function Kpis({ resource }) {
  if (resource.error) return <ErrorState onRetry={resource.retry} />;
  const totals = resource.data?.totals;
  return (
    <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
      <KpiCard label={text.dataUsed} value={formatGb(totals?.dataUsedGb ?? 0)} loading={!totals} hero />
      <KpiCard label={text.dataPaid} value={formatGb(totals?.dataPaidGb ?? 0)} loading={!totals} />
      <KpiCard label={text.utilisation} value={copy.units.percent(totals?.utilisationPercent ?? 0)} loading={!totals} />
      <KpiCard label={text.unused} value={formatGb(totals?.unusedGb ?? 0)} loading={!totals} />
    </div>
  );
}

function DailyChart({ resource }) {
  return (
    <DataState resource={resource} skeleton={<ChartSkeleton height={240} />} isEmpty={(data) => !data.daily.length || data.daily.every((point) => point.dataGb === 0)} emptyMessage={text.noUsage}>
      {(data) => (
        <>
          <LineChart data={data.daily} xKey="date" yKey="dataGb" label={text.daily} formatX={formatShortDate} formatY={formatGb} height={240} />
          <table className="sr-only">
            <caption>{text.daily}</caption>
            <thead><tr><th scope="col">{copy.columns.date}</th><th scope="col">{text.dataUsed}</th></tr></thead>
            <tbody>{data.daily.map((point) => <tr key={point.date}><th scope="row">{formatShortDate(point.date)}</th><td>{formatGb(point.dataGb)}</td></tr>)}</tbody>
          </table>
        </>
      )}
    </DataState>
  );
}

function UsageTable({ resource, rowsKey, title, emptyMessage }) {
  return (
    <DataTable
      caption={title}
      columns={simColumns}
      rows={resource.data?.[rowsKey] ?? null}
      getRowKey={(row) => row.id}
      getRowHref={(row) => `/sims/${row.id}`}
      error={resource.error}
      onRetry={resource.retry}
      busy={resource.loading}
      emptyMessage={emptyMessage}
      skeletonRows={5}
    />
  );
}

export function UsageView() {
  const [filters, setFilters] = useState({ days: "30", department: "", plan: "" });
  const resource = useResource(() => getUsageAnalytics({ days: Number(filters.days), department: filters.department, plan: filters.plan }), [filters.days, filters.department, filters.plan]);
  const setFilter = (key) => (value) => setFilters((current) => ({ ...current, [key]: value }));

  return (
    <>
      <PageHeader
        title={copy.pages.usage.title}
        actions={<FilterSelect label={text.period} value={filters.days} options={periodOptions} includeAll={false} onChange={setFilter("days")} className="min-w-36" />}
      />
      <div className="flex flex-wrap items-center gap-2">
        <FilterSelect label={text.department} value={filters.department} options={departmentOptions} onChange={setFilter("department")} />
        <FilterSelect label={text.plan} value={filters.plan} options={planOptions} onChange={setFilter("plan")} />
      </div>
      <Kpis resource={resource} />
      <SectionCard title={text.daily}>
        <DailyChart resource={resource} />
      </SectionCard>
      <div className="grid gap-4 lg:grid-cols-2">
        <SectionCard title={text.byDepartment}>
          <DataState resource={resource} skeleton={<ListSkeleton rows={5} />} isEmpty={(data) => !data.byDepartment.length} emptyMessage={text.noDepartment}>
            {(data) => <BarList items={data.byDepartment.map((row) => ({ key: row.name, label: row.name, value: row.dataUsedGb }))} formatValue={formatGb} />}
          </DataState>
        </SectionCard>
        <SectionCard title={text.byPlan}>
          <DataState resource={resource} skeleton={<ListSkeleton rows={5} />} isEmpty={(data) => !data.byPlan.length} emptyMessage={text.noPlan}>
            {(data) => <BarList items={data.byPlan.map((row) => ({ key: row.name, label: row.name, value: row.dataUsedGb }))} formatValue={formatGb} />}
          </DataState>
        </SectionCard>
      </div>
      <SectionCard title={text.topSims} flush>
        <UsageTable resource={resource} rowsKey="topSims" title={text.topSims} emptyMessage={text.noSims} />
      </SectionCard>
      <SectionCard title={text.leastUsedSims} flush>
        <UsageTable resource={resource} rowsKey="leastUsedSims" title={text.leastUsedSims} emptyMessage={text.noSims} />
      </SectionCard>
    </>
  );
}
