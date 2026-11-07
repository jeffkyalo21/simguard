"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { ChevronDown, ShieldCheck } from "lucide-react";
import { DataTable } from "@/components/data/data-table";
import { FilterSelect } from "@/components/data/filter-select";
import { Pagination } from "@/components/data/pagination";
import { SearchInput } from "@/components/data/search-input";
import { useAuth } from "@/components/auth/auth-provider";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useResource } from "@/hooks/use-resource";
import { ROLES } from "@/lib/auth/core";
import { listAuditLogs } from "@/lib/data";
import { dayToUtc, formatDateTime, formatKes, formatPhone } from "@/lib/format";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.auditLog;
const SEARCH_DELAY_MS = 250;
const FILTER_KEYS = ["actor", "action", "from", "to"];
const RANGE_ERROR_ID = "audit-range-error";

// System actions (user accounts and scoring runs) are all Admin sees; the
// Auditor also sees the organisation actions. The API enforces the split.
const SYSTEM_ACTIONS = [
  "user.created", "user.disabled", "user.enabled", "user.role_changed", "user.password_reset", "user.mfa_reset",
  "scoring.run_requested", "scoring.run_failed",
];
const ORGANISATION_ACTIONS = [
  "sim.created", "sim.updated", "sim.status_changed", "sim.tier_changed", "sim.deleted",
  "allocation.created", "allocation.returned", "alert.status_changed",
  "carrier.created", "carrier.renamed", "carrier.deleted",
  "department.created", "department.updated", "department.deleted",
  "employee.created", "employee.updated", "employee.deleted",
  "import.committed", "tier_recommendation.run_started", "tier_recommendation.settings_changed", "tier_recommendation.dismissed",
];
const toOptions = (actions) => actions.map((value) => ({ value, label: text.actions[value] ?? value }));

// Keys that name the record changed rather than a value that changed.
const CONTEXT_KEYS = ["alertId", "allocationId"];
const TIME_KEYS = ["assignmentDate", "returnDate", "resolvedAt"];

const EMPTY_PAGE = { items: [], page: 1, pageSize: 20, total: 0 };

function writeQuery(pathname, changes, { replace = false } = {}) {
  const params = new URLSearchParams(window.location.search);
  for (const [key, value] of Object.entries(changes)) {
    if (value === "" || value == null || (key === "page" && Number(value) <= 1)) params.delete(key);
    else params.set(key, String(value));
  }
  const query = params.toString();
  const url = query ? `${pathname}?${query}` : pathname;
  if (replace) window.history.replaceState(null, "", url);
  else window.history.pushState(null, "", url);
}

/** Readable label for a row; alert status changes say what happened. */
export function auditActionLabel(row) {
  if (row.action === "alert.status_changed") {
    if (row.newValues?.status === "cleared") return text.actions.alertCleared;
    if (row.oldValues?.status === "cleared") return text.actions.alertReopened;
  }
  return text.actions[row.action] ?? row.action;
}

function formatValue(key, value) {
  if (value === null || value === undefined || value === "") return text.none;
  if (text.refs[key]) return text.refs[key](value);
  if (TIME_KEYS.includes(key)) return formatDateTime(value);
  if (key === "phoneNumber") return formatPhone(value);
  if (key === "monthlyCost") return formatKes(value);
  if (key === "simStatus") return copy.simStatus[value] ?? value;
  if (key === "status") return copy.simStatus[value] ?? copy.alertStatus[value] ?? value;
  if (key === "resolution") {
    const label = copy.alertResolution[value] ?? value;
    return label.charAt(0).toUpperCase() + label.slice(1);
  }
  if (key === "tier") return copy.tiers[value] ?? value;
  return String(value);
}

/**
 * The record a row is about (an alert or allocation), then one line per
 * field that changed, with its value before and after. A created record
 * lists every field with nothing before it.
 */
function describeChanges(row) {
  const before = row.oldValues ?? {};
  const after = row.newValues ?? {};
  const keys = [...new Set([...Object.keys(before), ...Object.keys(after)])];
  const context = [];
  const lines = [];

  for (const key of keys) {
    if (key === "id" && row.affectedSimId) continue;
    const same = JSON.stringify(before[key] ?? null) === JSON.stringify(after[key] ?? null);

    if (CONTEXT_KEYS.includes(key) && same) {
      context.push({ key, value: formatValue(key, after[key]) });
    } else if (!same) {
      lines.push({
        key,
        label: text.fields[key] ?? key,
        before: formatValue(key, before[key]),
        after: formatValue(key, after[key]),
      });
    }
  }

  return { context, lines };
}

function Changes({ row }) {
  const { context, lines } = describeChanges(row);
  const headingId = `audit-${row.id}-heading`;

  return (
    <div id={`audit-${row.id}-changes`} className="rounded-lg bg-foreground/4 px-4 py-3 text-sm whitespace-normal">
      <p id={headingId} className="font-medium">
        {text.whatChanged}
        {context.map((item) => (
          <span key={item.key} className="font-normal text-muted-foreground">{text.contextSeparator}{item.value}</span>
        ))}
      </p>
      {lines.length ? (
        <table aria-labelledby={headingId} className="mt-2 w-full max-w-3xl border-collapse text-left">
          <thead>
            <tr className="text-xs text-muted-foreground">
              <th scope="col" className="w-40 py-1 pr-4 font-medium">{text.field}</th>
              <th scope="col" className="py-1 pr-4 font-medium">{text.before}</th>
              <th scope="col" className="py-1 font-medium">{text.after}</th>
            </tr>
          </thead>
          <tbody>
            {lines.map((line) => (
              <tr key={line.key} className="align-top">
                <th scope="row" className="py-1 pr-4 font-normal text-muted-foreground">{line.label}</th>
                <td className="py-1 pr-4 break-words">{line.before}</td>
                <td className="py-1 break-words">{line.after}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : (
        <p className="mt-2 text-muted-foreground">{text.noChanges}</p>
      )}
    </div>
  );
}

function DayField({ id, label, value, onChange, invalid }) {
  return (
    <div className="flex items-center gap-2">
      <label htmlFor={id} className="text-sm text-muted-foreground">
        {label}
      </label>
      <Input
        id={id}
        type="date"
        value={value}
        onChange={(event) => onChange(event.target.value)}
        aria-invalid={invalid || undefined}
        aria-describedby={invalid ? RANGE_ERROR_ID : undefined}
        className="glass-field h-10 w-40 px-3"
      />
    </div>
  );
}

export function AuditLogView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const query = Object.fromEntries(FILTER_KEYS.map((key) => [key, searchParams.get(key) ?? ""]));
  const page = Math.max(Number(searchParams.get("page")) || 1, 1);
  const { role } = useAuth();
  const actionOptions = toOptions(role === ROLES.admin ? SYSTEM_ACTIONS : [...ORGANISATION_ACTIONS, ...SYSTEM_ACTIONS]);
  const invalidRange = Boolean(query.from && query.to && query.from > query.to);

  // The search box is local while typing and follows the URL otherwise.
  const [search, setSearch] = useState(query.actor);
  const [urlActor, setUrlActor] = useState(query.actor);
  if (query.actor !== urlActor) {
    setUrlActor(query.actor);
    setSearch(query.actor);
  }

  useEffect(() => {
    if (search === urlActor) return undefined;
    const timer = setTimeout(() => writeQuery(pathname, { actor: search.trim(), page: 1 }, { replace: true }), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [search, urlActor, pathname]);

  const resource = useResource(
    () => (invalidRange
      ? Promise.resolve(EMPTY_PAGE)
      : listAuditLogs({
        actorEmail: query.actor,
        action: query.action,
        from: dayToUtc(query.from, "start"),
        to: dayToUtc(query.to, "end"),
        page,
      })),
    [query.actor, query.action, query.from, query.to, page]
  );
  const result = resource.data;

  const [open, setOpen] = useState(() => new Set());
  function toggle(id) {
    setOpen((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  const setFilter = (key) => (value) => writeQuery(pathname, { [key]: value, page: 1 });
  const hasFilters = FILTER_KEYS.some((key) => query[key]);
  function clearFilters() {
    setSearch("");
    writeQuery(pathname, { actor: "", action: "", from: "", to: "", page: 1 });
  }

  const columns = [
    { key: "time", header: copy.columns.time, cell: (row) => <span className="tabular-nums">{formatDateTime(row.timestamp)}</span> },
    { key: "user", header: copy.columns.user, cell: (row) => row.actorEmail },
    { key: "action", header: copy.columns.action, cell: auditActionLabel },
    {
      key: "sim",
      header: copy.columns.sim,
      cell: (row) => row.affectedSimId ? (
        <Link href={`/sims/${row.affectedSimId}`} className="rounded-sm font-medium underline-offset-4 hover:underline">
          {formatPhone(row.affectedSimPhoneNumber)}
        </Link>
      ) : copy.common.noValue,
    },
    {
      key: "changes",
      header: copy.columns.changes,
      className: "w-40 text-right",
      cell: (row) => {
        const expanded = open.has(row.id);
        return (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-9 px-3"
            aria-expanded={expanded}
            aria-controls={expanded ? `audit-${row.id}-changes` : undefined}
            onClick={() => toggle(row.id)}
          >
            {expanded ? text.hideChanges : text.showChanges}
            <span className="sr-only">{text.rowContext(auditActionLabel(row), formatDateTime(row.timestamp))}</span>
            <ChevronDown
              aria-hidden="true"
              className={cn("transition-transform duration-150 motion-reduce:transition-none", expanded && "rotate-180")}
            />
          </Button>
        );
      },
    },
  ];

  return (
    <>
      <div className="flex flex-col gap-2">
        <PageHeader title={copy.pages.auditLog.title} />
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <ShieldCheck className="size-4 shrink-0" aria-hidden="true" />
          {role === ROLES.admin ? text.roleNote.Admin : text.roleNote.Auditor}
        </p>
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <SearchInput
            label={text.searchLabel}
            placeholder={text.searchPlaceholder}
            value={search}
            onChange={setSearch}
            className="w-full sm:w-64"
          />
          <FilterSelect label={text.actionFilter} value={query.action} options={actionOptions} onChange={setFilter("action")} />
          <DayField id="audit-from" label={text.from} value={query.from} onChange={setFilter("from")} invalid={invalidRange} />
          <DayField id="audit-to" label={text.to} value={query.to} onChange={setFilter("to")} invalid={invalidRange} />
          <p className="ml-auto text-sm text-muted-foreground tabular-nums" aria-live="polite">
            {result && !invalidRange ? text.count(result.total) : " "}
          </p>
        </div>
        {invalidRange && (
          <p id={RANGE_ERROR_ID} role="alert" className="text-sm text-risk-flagged-foreground">
            {text.rangeError}
          </p>
        )}
      </div>

      <GlassCard variant="strong" className="flex flex-col gap-0 overflow-hidden p-0">
        <DataTable
          caption={copy.pages.auditLog.title}
          columns={columns}
          rows={result?.items ?? null}
          getRowKey={(row) => row.id}
          renderExpanded={(row) => (open.has(row.id) ? <Changes row={row} /> : null)}
          error={resource.error}
          onRetry={resource.retry}
          busy={resource.loading}
          skeletonRows={8}
          emptyMessage={invalidRange ? text.rangeError : hasFilters ? text.noMatch : copy.pages.auditLog.empty}
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
    </>
  );
}
