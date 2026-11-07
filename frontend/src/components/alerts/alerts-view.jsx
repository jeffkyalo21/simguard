"use client";

import { usePathname, useSearchParams } from "next/navigation";
import { DataTable } from "@/components/data/data-table";
import { FilterSelect } from "@/components/data/filter-select";
import { Pagination } from "@/components/data/pagination";
import { RiskBar } from "@/components/data/risk-bar";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { StatusBadge } from "@/components/shared/status-badge";
import { useResource } from "@/hooks/use-resource";
import { listAlerts, referenceData } from "@/lib/data";
import { formatDate, formatDateTime, formatPhone } from "@/lib/format";
import { copy } from "@/lib/copy";

const FILTER_KEYS = ["status", "type", "minScore", "sort"];
const statusOptions = ["flagged", "escalated", "cleared"].map((value) => ({ value, label: copy.alertStatus[value] }));
const typeOptions = referenceData.alertTypes.map((value) => ({ value, label: copy.alertTypes[value] }));
const scoreOptions = [{ value: "0.8", label: copy.alerts.minimumScore }];
const sortOptions = [
  { value: "createdAt", label: copy.alerts.newest },
  { value: "riskScore", label: copy.alerts.highestRisk },
];

function writeQuery(pathname, changes) {
  const params = new URLSearchParams(window.location.search);
  Object.entries(changes).forEach(([key, value]) => {
    if (!value || (key === "page" && Number(value) <= 1) || (key === "sort" && value === "createdAt")) params.delete(key);
    else params.set(key, String(value));
  });
  const query = params.toString();
  window.history.pushState(null, "", query ? `${pathname}?${query}` : pathname);
}

export function AlertsView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const query = Object.fromEntries(FILTER_KEYS.map((key) => [key, searchParams.get(key) ?? ""]));
  const page = Math.max(Number(searchParams.get("page")) || 1, 1);
  const resource = useResource(
    () => listAlerts({ ...query, page, sort: query.sort || "createdAt" }),
    [query.status, query.type, query.minScore, query.sort, page]
  );
  const result = resource.data;
  const hasFilters = FILTER_KEYS.some((key) => query[key]);
  const updateFilter = (key) => (value) => writeQuery(pathname, { [key]: value, page: 1 });
  const clearFilters = () => writeQuery(pathname, { status: "", type: "", minScore: "", sort: "", page: 1 });

  const columns = [
    { key: "sim", header: copy.columns.number, link: true, cell: (alert) => formatPhone(alert.simPhoneNumber) },
    { key: "type", header: copy.columns.type, cell: (alert) => copy.alertTypes[alert.type] ?? alert.type },
    { key: "risk", header: copy.columns.risk, cell: (alert) => <RiskBar score={alert.riskScore} /> },
    { key: "status", header: copy.columns.status, cell: (alert) => <StatusBadge status={alert.status} /> },
    { key: "date", header: copy.columns.detected, cell: (alert) => (alert.detectedOn ? formatDate(alert.detectedOn) : formatDateTime(alert.createdAt)) },
    {
      key: "employee", header: copy.columns.employee, cell: (alert) => (
        <span className="block min-w-36">
          <span className="block">{alert.employeeName ?? <span className="text-muted-foreground">{copy.alerts.employeeUnavailable}</span>}</span>
          <span className="text-xs text-muted-foreground">{alert.departmentName ?? copy.alerts.departmentUnavailable}</span>
        </span>
      ),
    },
  ];

  return (
    <>
      <PageHeader title={copy.pages.alerts.title} />
      <div className="flex flex-wrap items-center gap-2">
        <FilterSelect label={copy.alerts.statusFilter} value={query.status} options={statusOptions} onChange={updateFilter("status")} />
        <FilterSelect label={copy.alerts.typeFilter} value={query.type} options={typeOptions} onChange={updateFilter("type")} />
        <FilterSelect label={copy.alerts.scoreFilter} value={query.minScore} options={scoreOptions} onChange={updateFilter("minScore")} />
        <FilterSelect label={copy.alerts.sort} value={query.sort || "createdAt"} options={sortOptions} includeAll={false} onChange={updateFilter("sort")} />
      </div>
      <GlassCard variant="strong" className="flex flex-col gap-0 overflow-hidden p-0">
        <DataTable
          caption={copy.pages.alerts.title}
          columns={columns}
          rows={result?.items ?? null}
          getRowKey={(alert) => alert.id}
          getRowHref={(alert) => `/alerts/${alert.id}`}
          error={resource.error}
          onRetry={resource.retry}
          busy={resource.loading}
          skeletonRows={8}
          emptyMessage={hasFilters ? copy.alerts.noMatch : copy.pages.alerts.empty}
          emptyAction={hasFilters && <button type="button" onClick={clearFilters} className="touch-target rounded-lg border border-input px-4 py-2 text-sm font-medium focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring">{copy.actions.clearFilters}</button>}
        />
        {result && result.total > 0 && (
          <div className="border-t border-border px-5 py-3">
            <Pagination page={result.page} pageSize={result.pageSize} total={result.total} onPageChange={(next) => writeQuery(pathname, { page: next })} />
          </div>
        )}
      </GlassCard>
    </>
  );
}
