"use client";

import { useState } from "react";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { toast } from "sonner";
import { CircleCheck, History, Plus, Undo2 } from "lucide-react";
import { AllocationFormDialog } from "@/components/allocations/allocation-form-dialog";
import { DataTable } from "@/components/data/data-table";
import { FilterSelect } from "@/components/data/filter-select";
import { Pagination } from "@/components/data/pagination";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { ConfirmDialog } from "@/components/shared/confirm-dialog";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { Button } from "@/components/ui/button";
import { usePermission } from "@/hooks/use-permission";
import { useResource } from "@/hooks/use-resource";
import { ApiError, errorMessage } from "@/lib/api";
import { endAllocation, listAllocations } from "@/lib/data";
import { formatDate, formatPhone } from "@/lib/format";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.allocations;
const FILTER_KEYS = ["status"];

const statusOptions = ["active", "ended"].map((value) => ({ value, label: text.status[value] }));

function writeQuery(pathname, changes) {
  const params = new URLSearchParams(window.location.search);
  for (const [key, value] of Object.entries(changes)) {
    if (value === "" || value == null || (key === "page" && Number(value) <= 1)) params.delete(key);
    else params.set(key, String(value));
  }
  const query = params.toString();
  window.history.pushState(null, "", query ? `${pathname}?${query}` : pathname);
}

// Neutral tones with an icon and text: whether an allocation is active is
// not a risk, so it never borrows risk colours.
function AllocationBadge({ active }) {
  const Icon = active ? CircleCheck : History;
  return (
    <span
      className={cn(
        "inline-flex h-6 items-center gap-1 rounded-full px-2.5 text-xs font-medium whitespace-nowrap",
        active ? "bg-foreground/8 text-foreground" : "border border-dashed border-input text-muted-foreground"
      )}
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {active ? text.status.active : text.status.ended}
    </span>
  );
}

export function AllocationsView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const query = Object.fromEntries(FILTER_KEYS.map((key) => [key, searchParams.get(key) ?? ""]));
  const page = Math.max(Number(searchParams.get("page")) || 1, 1);

  const resource = useResource(
    () => listAllocations({ status: query.status, page }),
    [query.status, page]
  );
  const result = resource.data;

  const [formOpen, setFormOpen] = useState(false);
  // The confirm dialog keeps the allocation it opened with while closing.
  const [ending, setEnding] = useState({ open: false, allocation: null });
  const [busy, setBusy] = useState(false);
  const editing = usePermission("allocations.edit");

  async function confirmEnd() {
    const { allocation } = ending;
    setBusy(true);
    try {
      await endAllocation(allocation.id);
      toast.success(text.toasts.ended(formatPhone(allocation.simPhoneNumber)));
    } catch (error) {
      toast.error(errorMessage(error, { 409: text.toasts.alreadyEnded }));
    } finally {
      setBusy(false);
      setEnding((current) => ({ ...current, open: false }));
    }
  }

  const setFilter = (key) => (value) => writeQuery(pathname, { [key]: value, page: 1 });
  const hasFilters = FILTER_KEYS.some((key) => query[key]);
  function clearFilters() {
    writeQuery(pathname, { status: "", page: 1 });
  }

  const columns = [
    {
      key: "sim",
      header: copy.columns.sim,
      cell: (item) => (
        <Link href={`/sims/${item.simId}`} className="rounded-sm font-medium underline-offset-4 hover:underline">
          {formatPhone(item.simPhoneNumber)}
        </Link>
      ),
    },
    { key: "employee", header: copy.columns.employee, cell: (item) => item.employeeName },
    { key: "department", header: copy.columns.department, cell: (item) => item.departmentName ?? copy.common.noValue },
    { key: "start", header: copy.columns.startDate, cell: (item) => formatDate(item.assignmentDate) },
    {
      key: "end",
      header: copy.columns.endDate,
      cell: (item) => item.returnDate ? formatDate(item.returnDate) : <span className="text-muted-foreground">{text.ongoing}</span>,
    },
    { key: "status", header: copy.columns.status, cell: (item) => <AllocationBadge active={item.isActive} /> },
    {
      key: "actions",
      header: copy.columns.actions,
      srOnlyHeader: true,
      className: "w-24 pr-3 text-right",
      cell: (item) => item.isActive && (
        <DisabledReason reason={editing.reason ?? ""} disabled={!editing.allowed}>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-9 px-3"
            disabled={!editing.allowed}
            onClick={() => setEnding({ open: true, allocation: item })}
          >
            <Undo2 aria-hidden="true" />
            {text.end}
            <span className="sr-only">{text.endFor(formatPhone(item.simPhoneNumber), item.employeeName)}</span>
          </Button>
        </DisabledReason>
      ),
    },
  ];

  const target = ending.allocation;

  return (
    <>
      <PageHeader
        title={copy.pages.allocations.title}
        actions={
          <DisabledReason reason={editing.reason ?? ""} disabled={!editing.allowed}>
            <Button onClick={() => setFormOpen(true)} size="lg" className="h-10 px-4" disabled={!editing.allowed}>
              <Plus aria-hidden="true" />
              {text.add}
            </Button>
          </DisabledReason>
        }
      />

      <div className="flex flex-wrap items-center gap-2">
        <FilterSelect label={text.statusFilter} value={query.status} options={statusOptions} onChange={setFilter("status")} />
        <p className="ml-auto text-sm text-muted-foreground tabular-nums" aria-live="polite">
          {result ? text.count(result.total) : " "}
        </p>
      </div>

      <GlassCard variant="strong" className="flex flex-col gap-0 overflow-hidden p-0">
        <DataTable
          caption={copy.pages.allocations.title}
          columns={columns}
          rows={result?.items ?? null}
          getRowKey={(item) => item.id}
          error={resource.error}
          onRetry={resource.retry}
          busy={resource.loading}
          skeletonRows={8}
          emptyMessage={hasFilters ? text.noMatch : copy.pages.allocations.empty}
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
              onPageChange={(next) => writeQuery(pathname, { page: next })}
            />
          </div>
        )}
      </GlassCard>

      <AllocationFormDialog open={formOpen} onOpenChange={setFormOpen} />
      <ConfirmDialog
        open={ending.open}
        onOpenChange={(open) => setEnding((current) => ({ ...current, open }))}
        title={text.confirm.title}
        description={target ? text.confirm.text(formatPhone(target.simPhoneNumber), target.employeeName) : ""}
        confirmLabel={text.confirm.action}
        busy={busy}
        onConfirm={confirmEnd}
      />
    </>
  );
}
