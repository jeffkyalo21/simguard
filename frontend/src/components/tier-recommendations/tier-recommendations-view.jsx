"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname, useSearchParams } from "next/navigation";
import { toast } from "sonner";
import { ArrowDown, ArrowUp, Check, Play, X } from "lucide-react";
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
import {
  applyTierRecommendation,
  dismissTierRecommendation,
  getTierRecommendationStatus,
  listTierRecommendations,
  runTierRecommendations,
} from "@/lib/data";
import { bumpDataVersion } from "@/lib/data-version";
import { formatBytes, formatDate, formatDateTime, formatPhone } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.tierRecommendations;
const STATUSES = ["open", "applied", "dismissed"];
const DEFAULT_STATUS = "open";
// While a run is in progress the page checks again this often.
const POLL_MS = 3000;

const statusOptions = STATUSES.map((value) => ({ value, label: text.status[value] }));

function writeQuery(pathname, changes) {
  const params = new URLSearchParams(window.location.search);
  for (const [key, value] of Object.entries(changes)) {
    if (value === "" || value == null || (key === "page" && Number(value) <= 1)) params.delete(key);
    else params.set(key, String(value));
  }
  const query = params.toString();
  window.history.pushState(null, "", query ? `${pathname}?${query}` : pathname);
}

const tierName = (tier) => copy.tiers[tier] ?? tier;

// Built from the forecast fields so the wording and units match the rest of
// the UI; the backend's own reason sentence is for the audit trail.
function reasonFor(item) {
  const amount = formatBytes(Math.abs(item.projectedOverageMb ?? 0) * 1_000_000);
  const tier = tierName(item.currentTier);
  if (item.recommendation === "upgrade") {
    return item.daysToDepletion != null
      ? text.reason.overWithDays(amount, tier, item.daysToDepletion)
      : text.reason.over(amount, tier);
  }
  return text.reason.unused(amount, tier);
}

// Neutral tones: a tier change is a cost question, not a risk.
function ChangeBadge({ recommendation }) {
  const Icon = recommendation === "upgrade" ? ArrowUp : ArrowDown;
  return (
    <span className="inline-flex h-6 items-center gap-1 rounded-full bg-foreground/8 px-2.5 text-xs font-medium whitespace-nowrap">
      <Icon className="size-3.5" aria-hidden="true" />
      {text.change[recommendation] ?? recommendation}
    </span>
  );
}

/** Last run, next allowed run and the Run button. */
function RunPanel({ status, canRun }) {
  const [busy, setBusy] = useState(false);
  const data = status.data;
  const running = Boolean(data?.running);
  const last = data?.lastRun;

  // Reload the page's data until the run has finished.
  useEffect(() => {
    if (!running) return undefined;
    const timer = setTimeout(bumpDataVersion, POLL_MS);
    return () => clearTimeout(timer);
  }, [running, data]);

  async function run() {
    setBusy(true);
    try {
      await runTierRecommendations();
      toast.success(text.toasts.started);
    } catch (error) {
      // 409: a run is in progress, or the interval has not passed yet.
      const next = error instanceof ApiError ? error.extensions.nextAllowedRunDate : null;
      toast.error(errorMessage(error, { 409: next ? text.toasts.tooSoon(formatDate(next)) : text.toasts.busy }));
    } finally {
      setBusy(false);
    }
  }

  const reason = canRun.reason ?? (running ? text.runningReason : null);

  return (
    <GlassCard variant="strong" className="flex flex-wrap items-center justify-between gap-4 p-5">
      <div className="flex min-w-0 flex-col gap-1 text-sm" aria-live="polite">
        {status.error ? (
          <p className="text-risk-flagged-foreground">{copy.errors.load}</p>
        ) : !data ? (
          <p className="text-muted-foreground">{text.loadingStatus}</p>
        ) : running ? (
          <p className="font-medium">{text.running}</p>
        ) : last ? (
          <>
            <p className="font-medium">{text.lastRun(text.runStatus[last.status] ?? last.status, formatDateTime(last.finishedAt ?? last.startedAt))}</p>
            <p className="text-muted-foreground">{text.lastRunCounts(last.simsEvaluated, last.recommendationsCreated)}</p>
          </>
        ) : (
          <p className="font-medium">{text.neverRun}</p>
        )}
        {data?.nextAllowedRunDate && !running && (
          <p className="text-muted-foreground">{text.nextAllowed(formatDate(data.nextAllowedRunDate), data.intervalDays)}</p>
        )}
      </div>
      <DisabledReason reason={reason ?? ""} disabled={reason !== null}>
        <Button className="h-10 px-4" disabled={reason !== null || busy || !data} onClick={run}>
          <Play aria-hidden="true" />
          {text.run}
        </Button>
      </DisabledReason>
    </GlassCard>
  );
}

export function TierRecommendationsView() {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const statusParam = searchParams.get("status");
  const filter = STATUSES.includes(statusParam) ? statusParam : DEFAULT_STATUS;
  const page = Math.max(Number(searchParams.get("page")) || 1, 1);

  const editing = usePermission("tierRecommendations.edit");
  const status = useResource(() => getTierRecommendationStatus(), []);
  const resource = useResource(() => listTierRecommendations({ status: filter, page }), [filter, page]);
  const result = resource.data;

  // The confirm dialog keeps the recommendation and action it opened with.
  const [confirm, setConfirm] = useState({ open: false, item: null, action: "apply" });
  const [busy, setBusy] = useState(false);

  async function confirmAction() {
    const { item, action } = confirm;
    setBusy(true);
    try {
      if (action === "apply") {
        await applyTierRecommendation(item.id);
        toast.success(text.toasts.applied(formatPhone(item.simPhoneNumber), tierName(item.recommendedTier)));
      } else {
        await dismissTierRecommendation(item.id);
        toast.success(text.toasts.dismissed(formatPhone(item.simPhoneNumber)));
      }
    } catch (error) {
      toast.error(errorMessage(error, { 409: text.toasts.notOpen }));
    } finally {
      setBusy(false);
      setConfirm((current) => ({ ...current, open: false }));
    }
  }

  const open = filter === "open";
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
    { key: "change", header: text.columns.change, cell: (item) => <ChangeBadge recommendation={item.recommendation} /> },
    {
      key: "tiers",
      header: text.columns.tiers,
      cell: (item) => text.tierMove(tierName(item.currentTier), tierName(item.recommendedTier)),
    },
    { key: "reason", header: text.columns.reason, className: "min-w-64 whitespace-normal", cell: reasonFor },
    {
      key: "date",
      header: open ? text.columns.created : text.columns.closed,
      cell: (item) => formatDate(open ? item.createdAt : item.appliedAt ?? item.dismissedAt ?? item.createdAt),
    },
    ...(open
      ? [
          {
            key: "actions",
            header: copy.columns.actions,
            srOnlyHeader: true,
            className: "w-56 pr-3 text-right",
            cell: (item) => {
              const context = text.rowContext(formatPhone(item.simPhoneNumber));
              return (
                <div className="flex justify-end gap-2">
                  <DisabledReason reason={editing.reason ?? ""} disabled={!editing.allowed}>
                    <Button
                      type="button"
                      size="sm"
                      className="h-9 px-3"
                      disabled={!editing.allowed}
                      onClick={() => setConfirm({ open: true, item, action: "apply" })}
                    >
                      <Check aria-hidden="true" />
                      {text.apply}
                      <span className="sr-only">{context}</span>
                    </Button>
                  </DisabledReason>
                  <DisabledReason reason={editing.reason ?? ""} disabled={!editing.allowed}>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      className="h-9 px-3"
                      disabled={!editing.allowed}
                      onClick={() => setConfirm({ open: true, item, action: "dismiss" })}
                    >
                      <X aria-hidden="true" />
                      {text.dismiss}
                      <span className="sr-only">{context}</span>
                    </Button>
                  </DisabledReason>
                </div>
              );
            },
          },
        ]
      : []),
  ];

  const target = confirm.item;
  const applying = confirm.action === "apply";

  return (
    <>
      <PageHeader title={text.title} />
      <p className="-mt-3 max-w-2xl text-sm text-muted-foreground">{text.intro}</p>

      <RunPanel status={status} canRun={editing} />

      <div className="flex flex-wrap items-center gap-2">
        <FilterSelect
          label={text.statusFilter}
          value={filter}
          options={statusOptions}
          includeAll={false}
          onChange={(value) => writeQuery(pathname, { status: value === DEFAULT_STATUS ? "" : value, page: 1 })}
        />
        <p className="ml-auto text-sm text-muted-foreground tabular-nums" aria-live="polite">
          {result ? text.count(result.total) : " "}
        </p>
      </div>

      <GlassCard variant="strong" className="flex flex-col gap-0 overflow-hidden p-0">
        <DataTable
          caption={text.title}
          columns={columns}
          rows={result?.items ?? null}
          getRowKey={(item) => item.id}
          error={resource.error}
          onRetry={resource.retry}
          busy={resource.loading}
          skeletonRows={6}
          emptyMessage={text.empty[filter]}
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

      <ConfirmDialog
        open={confirm.open}
        onOpenChange={(value) => setConfirm((current) => ({ ...current, open: value }))}
        title={applying ? text.confirm.applyTitle : text.confirm.dismissTitle}
        description={
          target
            ? applying
              ? text.confirm.applyText(formatPhone(target.simPhoneNumber), tierName(target.currentTier), tierName(target.recommendedTier))
              : text.confirm.dismissText(formatPhone(target.simPhoneNumber))
            : ""
        }
        confirmLabel={applying ? text.apply : text.dismiss}
        busy={busy}
        onConfirm={confirmAction}
      />
    </>
  );
}
