"use client";

import Link from "next/link";
import { useState } from "react";
import { toast } from "sonner";
import { DataState } from "@/components/data/data-state";
import { DataTable } from "@/components/data/data-table";
import { LineChart } from "@/components/data/line-chart";
import { ProgressBar } from "@/components/data/progress-bar";
import { RiskBar } from "@/components/data/risk-bar";
import { SectionCard } from "@/components/data/section-card";
import { ChartSkeleton, ListSkeleton } from "@/components/data/skeletons";
import { PageHeader } from "@/components/layout/page-header";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { ErrorState } from "@/components/shared/error-state";
import { FormDialog } from "@/components/shared/form-dialog";
import { NotFoundContent } from "@/components/shared/not-found-content";
import { PageBreadcrumb } from "@/components/shared/page-breadcrumb";
import { RiskBadge } from "@/components/shared/risk-badge";
import { activityText } from "@/components/sims/activity-text";
import { Button } from "@/components/ui/button";
import { useResource } from "@/hooks/use-resource";
import { ApiError } from "@/lib/api";
import { getAlert, getSimRisk, listSimActivity, setAlertStatus } from "@/lib/data";
import { GATE_BANDS, formatDate, formatDateTime, formatPhone, formatShortDate } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.alertDetail;

function featureLabel(feature) {
  return copy.featureLabels[feature] ?? feature.replaceAll("_", " ");
}

function ReasonList({ reasons }) {
  return (
    <ul className="flex flex-col divide-y divide-border">
      {reasons.map((reason, index) => {
        const contribution = text.contribution(reason.contribution);
        return (
          <li key={`${reason.feature}-${index}`} className="grid gap-2 py-3 sm:grid-cols-[minmax(0,1fr)_9rem] sm:items-center">
            <div className="min-w-0">
              <p className="font-medium">{featureLabel(reason.feature)}</p>
              <p className="text-sm text-muted-foreground">{text.direction[reason.direction] ?? reason.direction}</p>
            </div>
            <div className="flex items-center gap-2">
              <ProgressBar value={reason.contribution * 100} max={40} label={featureLabel(reason.feature)} valueText={contribution} className="w-20" />
              <span className="text-sm tabular-nums">{contribution}</span>
            </div>
          </li>
        );
      })}
    </ul>
  );
}

function Investigation({ alert }) {
  // The history ends on the alert's last detection day, not today: the demo
  // scores are in 2025.
  const riskTo = alert.lastDetectedOn ?? alert.detectedOn ?? undefined;
  const risk = useResource(() => getSimRisk(alert.simId, { days: 90, to: riskTo }), [alert.simId, riskTo]);
  const activity = useResource(() => listSimActivity(alert.simId), [alert.simId]);
  const [dialog, setDialog] = useState({ open: false, resolution: null });
  const [busy, setBusy] = useState(false);
  const canClose = alert.status === "flagged" || alert.status === "escalated";
  const canReopen = alert.status === "cleared";
  const canConfirm = alert.status === "escalated";

  async function closeWithResolution(event) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const resolutionNote = String(form.get("resolutionNote") ?? "");
    if (resolutionNote.length > 500) return;
    setBusy(true);
    try {
      await setAlertStatus(alert.id, { status: "cleared", resolution: dialog.resolution, ...(resolutionNote ? { resolutionNote } : {}) });
      toast.success(dialog.resolution === "confirmedMisuse" ? text.toasts.confirmed : text.toasts.falsePositive);
      setDialog({ open: false, resolution: null });
    } catch {
      toast.error(copy.errors.generic);
    } finally {
      setBusy(false);
    }
  }

  async function changeStatus(status) {
    setBusy(true);
    try {
      await setAlertStatus(alert.id, { status });
      toast.success(status === "escalated" ? text.toasts.reopened : text.toasts.flagged);
    } catch {
      toast.error(copy.errors.generic);
    } finally {
      setBusy(false);
    }
  }

  const riskHistory = risk.data?.history ?? [];
  const showBands = riskHistory.some((point) => point.probability <= GATE_BANDS.low) && riskHistory.some((point) => point.probability >= GATE_BANDS.high);
  const recentColumns = [
    { key: "date", header: copy.columns.date, cell: (item) => formatDateTime(item.createdAt) },
    { key: "type", header: copy.columns.type, cell: (item) => copy.alertTypes[item.type] ?? item.type },
    { key: "risk", header: copy.columns.risk, cell: (item) => <RiskBar score={item.riskScore} /> },
    { key: "status", header: copy.columns.status, cell: (item) => <RiskBadge level={item.status} /> },
  ];
  const activityColumns = [
    { key: "time", header: copy.columns.time, cell: (entry) => formatDateTime(entry.occurredAt) },
    { key: "action", header: copy.columns.action, cell: activityText },
  ];

  return (
    <>
      <PageBreadcrumb items={[{ label: copy.pages.alerts.title, href: "/alerts" }, { label: copy.alertTypes[alert.type] ?? alert.type }]} />
      <PageHeader
        title={copy.alertTypes[alert.type] ?? alert.type}
        badge={<div className="flex flex-wrap items-center gap-2"><RiskBadge level={alert.status} /><span className="text-sm text-muted-foreground">{text.riskLevel}: {copy.alerts.riskLevel[alert.riskLevel] ?? alert.riskLevel}</span><RiskBar score={alert.riskScore} /></div>}
        actions={
          <>
            <DisabledReason reason={text.actionUnavailable} disabled={!canClose || busy}>
              <Button variant="outline" className="touch-target h-10 px-3" disabled={!canClose || busy} onClick={() => setDialog({ open: true, resolution: "confirmedMisuse" })}>{text.confirmedMisuse}</Button>
            </DisabledReason>
            <DisabledReason reason={text.actionUnavailable} disabled={!canClose || busy}>
              <Button variant="outline" className="touch-target h-10 px-3" disabled={!canClose || busy} onClick={() => setDialog({ open: true, resolution: "falsePositive" })}>{text.falsePositive}</Button>
            </DisabledReason>
            <DisabledReason reason={text.actionUnavailable} disabled={!canReopen || busy}>
              <Button variant="outline" className="touch-target h-10 px-3" disabled={!canReopen || busy} onClick={() => changeStatus("escalated")}>{text.reopen}</Button>
            </DisabledReason>
            <DisabledReason reason={text.actionUnavailable} disabled={!canConfirm || busy}>
              <Button variant="outline" className="touch-target h-10 px-3" disabled={!canConfirm || busy} onClick={() => changeStatus("flagged")}>{text.confirmFlagged}</Button>
            </DisabledReason>
          </>
        }
      />

      <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)]">
        <SectionCard title={text.whyFlagged}>
          <ReasonList reasons={alert.reasons ?? []} />
        </SectionCard>
        <SectionCard title={text.simAndEmployee}>
          <dl className="grid gap-x-5 sm:grid-cols-2">
            <div className="border-b border-border py-3 text-sm"><dt className="text-muted-foreground">{copy.columns.number}</dt><dd className="mt-1 font-medium"><Link href={`/sims/${alert.sim.id}`} className="rounded-sm hover:underline focus-visible:outline-2 focus-visible:outline-ring">{formatPhone(alert.sim.phoneNumber)}</Link></dd></div>
            <div className="border-b border-border py-3 text-sm"><dt className="text-muted-foreground">{text.employee}</dt><dd className="mt-1 font-medium">{alert.currentAllocation?.employeeName ?? text.noEmployee}</dd></div>
            <div className="border-b border-border py-3 text-sm"><dt className="text-muted-foreground">{text.department}</dt><dd className="mt-1 font-medium">{alert.currentAllocation?.departmentName ?? copy.common.noValue}</dd></div>
            <div className="border-b border-border py-3 text-sm"><dt className="text-muted-foreground">{text.iccid}</dt><dd className="mt-1 font-medium">{alert.sim.iccid}</dd></div>
            <div className="border-b border-border py-3 text-sm"><dt className="text-muted-foreground">{text.operator}</dt><dd className="mt-1 font-medium">{alert.sim.operator ?? copy.common.noValue}</dd></div>
            <div className="border-b border-border py-3 text-sm"><dt className="text-muted-foreground">{copy.columns.detected}</dt><dd className="mt-1 font-medium">{alert.detectedOn ? formatDate(alert.detectedOn) : formatDateTime(alert.createdAt)}</dd></div>
          </dl>
        </SectionCard>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <SectionCard title={text.riskOverTime}>
          <DataState resource={risk} skeleton={<ChartSkeleton height={240} />}>
            {(data) => data.history.length ? <LineChart data={data.history} xKey="date" yKey="probability" label={text.riskOverTime} formatX={formatShortDate} formatY={(value) => `${Math.round(value * 100)}%`} yDomain={[0, 1]} height={240} limits={showBands ? [{ y: GATE_BANDS.low, label: `${GATE_BANDS.low * 100}%` }, { y: GATE_BANDS.high, label: `${GATE_BANDS.high * 100}%` }] : []} /> : <p className="py-10 text-center text-sm text-muted-foreground">{text.noRisk}</p>}
          </DataState>
        </SectionCard>
        <SectionCard title={text.recentAlerts}>
          {alert.recentAlerts.length ? <DataTable caption={text.recentAlerts} columns={recentColumns} rows={alert.recentAlerts} getRowKey={(item) => item.id} getRowHref={(item) => `/alerts/${item.id}`} emptyMessage={text.noRecentAlerts} skeletonRows={3} /> : <p className="py-10 text-center text-sm text-muted-foreground">{text.noRecentAlerts}</p>}
        </SectionCard>
      </div>

      <SectionCard title={text.timeline}>
        <DataState resource={activity} skeleton={<ListSkeleton rows={4} />}>
          {(data) => <DataTable caption={text.timeline} columns={activityColumns} rows={data.items} getRowKey={(entry) => entry.id} emptyMessage={text.noTimeline} skeletonRows={3} />}
        </DataState>
      </SectionCard>

      <FormDialog
        open={dialog.open}
        onOpenChange={(open) => setDialog((current) => ({ ...current, open }))}
        title={dialog.resolution === "confirmedMisuse" ? text.confirmTitle : text.falseTitle}
        submitLabel={dialog.resolution === "confirmedMisuse" ? text.confirmedMisuse : text.falsePositive}
        busy={busy}
        onSubmit={closeWithResolution}
      >
        <p className="text-sm text-muted-foreground">{dialog.resolution === "confirmedMisuse" ? text.confirmText : text.falseText}</p>
        <label htmlFor="resolution-note" className="text-sm font-medium">{text.noteLabel}</label>
        <textarea id="resolution-note" name="resolutionNote" maxLength={500} rows={4} aria-describedby="resolution-note-hint" className="glass-field min-h-24 w-full resize-y rounded-xl px-3 py-2 text-sm outline-none focus-visible:ring-3 focus-visible:ring-ring/50" />
        <p id="resolution-note-hint" className="text-xs text-muted-foreground">{text.noteHint}</p>
      </FormDialog>
    </>
  );
}

export function AlertDetail({ id }) {
  const resource = useResource(() => getAlert(id), [id]);
  const alert = resource.data;

  if (resource.error instanceof ApiError && resource.error.status === 404) {
    return <NotFoundContent title={text.notFound} href="/alerts" action={copy.actions.viewAll} />;
  }
  if (resource.error) return <><h1 className="sr-only">{copy.pages.alertDetail.title}</h1><ErrorState error={resource.error} onRetry={resource.retry} /></>;
  if (!alert) return <div role="status" aria-busy="true" className="flex flex-col gap-5"><h1 className="sr-only">{copy.pages.alertDetail.title}</h1><ListSkeleton rows={8} /></div>;
  return <Investigation key={`${alert.id}-${alert.status}-${alert.resolvedAt ?? "open"}`} alert={alert} />;
}
