"use client";

import { useEffect, useState } from "react";
import { usePathname, useSearchParams } from "next/navigation";
import { MoreHorizontal, Pencil, Plus, Power, PowerOff } from "lucide-react";
import { DataTable } from "@/components/data/data-table";
import { FilterSelect } from "@/components/data/filter-select";
import { Pagination } from "@/components/data/pagination";
import { SearchInput } from "@/components/data/search-input";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { StatusBadge } from "@/components/shared/status-badge";
import { SimFormDialog } from "@/components/sims/sim-form-dialog";
import { SimStatusDialog, canChangeStatus, nextStatus } from "@/components/sims/sim-status-dialog";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useResource } from "@/hooks/use-resource";
import { useCarriers } from "@/hooks/use-reference";
import { listSims, referenceData, toGb } from "@/lib/data";
import { formatKes, formatPhone } from "@/lib/format";
import { copy } from "@/lib/copy";

const SEARCH_DELAY_MS = 250;
const FILTER_KEYS = ["q", "status", "operator"];

const statusOptions = referenceData.statuses.map((value) => ({ value, label: copy.simStatus[value] }));

/**
 * Writes the query to the URL with the History API, which Next.js syncs
 * with useSearchParams: no server round trip, and refresh, back and
 * forward keep the state.
 */
function writeQuery(pathname, current, changes, { replace = false } = {}) {
  const params = new URLSearchParams(current);

  for (const [key, value] of Object.entries(changes)) {
    if (value === "" || value == null || (key === "page" && Number(value) <= 1)) params.delete(key);
    else params.set(key, String(value));
  }

  const query = params.toString();
  const url = query ? `${pathname}?${query}` : pathname;
  if (replace) window.history.replaceState(null, "", url);
  else window.history.pushState(null, "", url);
}

function RowActions({ sim, onEdit, onStatus }) {
  const number = formatPhone(sim.phoneNumber);
  const target = nextStatus(sim);
  const blocked = !canChangeStatus(sim);
  const StatusIcon = target === "inactive" ? PowerOff : Power;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button variant="ghost" size="icon" className="touch-target size-9" aria-label={copy.actions.rowActions(number)}>
          <MoreHorizontal aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-44">
        <DropdownMenuItem onSelect={() => onEdit(sim)}>
          <Pencil aria-hidden="true" />
          {copy.actions.edit}
        </DropdownMenuItem>
        <DisabledReason reason={copy.sims.allocatedReason} disabled={blocked} focusable={false}>
          <DropdownMenuItem disabled={blocked} onSelect={() => onStatus(sim)} className="w-full">
            <StatusIcon aria-hidden="true" />
            {target === "inactive" ? copy.sims.setInactive : copy.sims.setAvailable}
            {blocked && <span className="sr-only">{copy.sims.allocatedReason}</span>}
          </DropdownMenuItem>
        </DisabledReason>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export function SimsView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const query = Object.fromEntries(FILTER_KEYS.map((key) => [key, searchParams.get(key) ?? ""]));
  const page = Math.max(Number(searchParams.get("page")) || 1, 1);

  // The search box is local while typing and follows the URL otherwise
  // (back and forward, or the top bar search).
  const [search, setSearch] = useState(query.q);
  const [urlQ, setUrlQ] = useState(query.q);
  if (query.q !== urlQ) {
    setUrlQ(query.q);
    setSearch(query.q);
  }

  useEffect(() => {
    if (search === urlQ) return undefined;
    const timer = setTimeout(
      () => writeQuery(pathname, window.location.search, { q: search.trim(), page: 1 }, { replace: true }),
      SEARCH_DELAY_MS
    );
    return () => clearTimeout(timer);
  }, [search, urlQ, pathname]);

  const resource = useResource(
    () => listSims({ q: query.q, status: query.status, carrierId: query.operator, page }),
    [query.q, query.status, query.operator, page]
  );
  const carriers = useCarriers();
  const operatorOptions = carriers.map((carrier) => ({ value: String(carrier.id), label: carrier.name }));
  const result = resource.data;

  // Dialogs keep the SIM they opened with, so nothing changes while closing.
  const [form, setForm] = useState({ open: false, sim: null });
  const [status, setStatus] = useState({ open: false, sim: null });

  function setFilter(key) {
    return (value) => writeQuery(pathname, window.location.search, { [key]: value, page: 1 });
  }

  function clearFilters() {
    setSearch("");
    writeQuery(pathname, "", {});
  }

  const hasFilters = FILTER_KEYS.some((key) => query[key]);

  const columns = [
    { key: "number", header: copy.columns.number, link: true, cell: (sim) => formatPhone(sim.phoneNumber) },
    { key: "employee", header: copy.columns.employee, cell: (sim) => sim.currentAllocation?.employeeName ?? <span className="text-muted-foreground">{copy.sims.unassigned}</span> },
    { key: "department", header: copy.columns.department, cell: (sim) => sim.currentAllocation?.departmentName ?? copy.common.noValue },
    { key: "tier", header: copy.columns.tier, cell: (sim) => copy.tiers[sim.tier ?? "none"] ?? sim.tier },
    {
      key: "bundle",
      header: copy.columns.bundle,
      className: "tabular-nums",
      cell: (sim) => (sim.allowance ? copy.units.gb(toGb(sim.allowance.dataMb)) : copy.common.noValue),
    },
    { key: "cost", header: copy.columns.monthlyCost, className: "tabular-nums", cell: (sim) => formatKes(sim.monthlyCost) },
    { key: "status", header: copy.columns.status, cell: (sim) => <StatusBadge status={sim.status} /> },
    {
      key: "actions",
      header: copy.columns.actions,
      srOnlyHeader: true,
      className: "w-14 pr-3 text-right",
      cell: (sim) => (
        <RowActions
          sim={sim}
          onEdit={(target) => setForm({ open: true, sim: target })}
          onStatus={(target) => setStatus({ open: true, sim: target })}
        />
      ),
    },
  ];

  return (
    <>
      <PageHeader
        title={copy.pages.sims.title}
        actions={
          <Button onClick={() => setForm({ open: true, sim: null })} size="lg" className="h-10 px-4">
            <Plus aria-hidden="true" />
            {copy.sims.add}
          </Button>
        }
      />

      <div className="flex flex-wrap items-center gap-2">
        <SearchInput
          label={copy.sims.searchLabel}
          placeholder={copy.sims.searchPlaceholder}
          value={search}
          onChange={setSearch}
          className="w-full sm:w-72"
        />
        <FilterSelect label={copy.simStatus.label} value={query.status} options={statusOptions} onChange={setFilter("status")} />
        <FilterSelect label={copy.sims.operator} value={query.operator} options={operatorOptions} onChange={setFilter("operator")} />
        <p className="ml-auto text-sm text-muted-foreground tabular-nums" aria-live="polite">
          {result ? copy.sims.count(result.total) : " "}
        </p>
      </div>

      <GlassCard variant="strong" className="flex flex-col gap-0 overflow-hidden p-0">
        <DataTable
          caption={copy.pages.sims.title}
          columns={columns}
          rows={result?.items ?? null}
          getRowKey={(sim) => sim.id}
          getRowHref={(sim) => `/sims/${sim.id}`}
          error={resource.error}
          onRetry={resource.retry}
          busy={resource.loading}
          skeletonRows={8}
          emptyMessage={hasFilters ? copy.sims.noMatch : copy.pages.sims.empty}
          emptyAction={
            hasFilters && (
              <Button variant="outline" onClick={clearFilters} className="h-10 px-4">
                {copy.actions.clearFilters}
              </Button>
            )
          }
        />
        {result && result.total > 0 && (
          <div className="border-t border-border px-5 py-3">
            <Pagination
              page={result.page}
              pageSize={result.pageSize}
              total={result.total}
              onPageChange={(next) => writeQuery(pathname, window.location.search, { page: next })}
            />
          </div>
        )}
      </GlassCard>

      <SimFormDialog
        open={form.open}
        sim={form.sim}
        onOpenChange={(open) => setForm((current) => ({ ...current, open }))}
      />
      <SimStatusDialog
        sim={status.sim}
        open={status.open}
        onOpenChange={(open) => setStatus((current) => ({ ...current, open }))}
      />
    </>
  );
}
