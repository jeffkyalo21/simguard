"use client";

import { CircleCheck, CircleX, TriangleAlert } from "lucide-react";
import { BarChart } from "@/components/data/bar-chart";
import { ChartTable } from "@/components/data/chart-table";
import { DonutChart } from "@/components/data/donut-chart";
import { SectionCard } from "@/components/data/section-card";
import { ChartSkeleton, ListSkeleton } from "@/components/data/skeletons";
import { GlassCard } from "@/components/glass/glass-card";
import { ErrorState } from "@/components/shared/error-state";
import { Button } from "@/components/ui/button";
import { usePolling } from "@/hooks/use-polling";
import { getSystemStatus } from "@/lib/data";
import { formatDateTime, formatNumber, formatShortDate } from "@/lib/format";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.admin.health;
const POLL_MS = 30_000;
// A database that answers slower than this is flagged as slow.
const SLOW_DATABASE_MS = 1000;

const STATE_ICONS = { ok: CircleCheck, warning: TriangleAlert, down: CircleX };

/** "3 d 4 h", "2 h 5 min", "12 min" or "40 s" from a number of seconds. */
function formatDuration(totalSeconds) {
  const seconds = Math.max(0, Math.round(totalSeconds));
  const t = text.duration;
  if (seconds < 60) return t.seconds(seconds);
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return t.minutes(minutes);
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return t.hours(hours, minutes % 60);
  return t.days(Math.floor(hours / 24), hours % 24);
}

/** The five tiles, each with a state (ok, warning, down), a headline and a detail. */
function buildTiles(d) {
  const ml = d.mlService;
  const scoring = d.scoring;
  const last = scoring.lastRun;

  const database = !d.database.ok
    ? { state: "down", headline: text.database.down, detail: text.database.downDetail }
    : d.database.responseMs > SLOW_DATABASE_MS
      ? { state: "warning", headline: text.database.slow(d.database.responseMs) }
      : { state: "ok", headline: text.database.ok(d.database.responseMs) };

  const mlTile = !ml.configured
    ? { state: "warning", headline: text.ml.notSet, detail: text.ml.notSetDetail }
    : ml.reachable
      ? { state: "ok", headline: text.ml.ok, detail: text.ml.okDetail(ml.modelVersion) }
      : { state: "down", headline: text.ml.down, detail: text.ml.downDetail };

  const email = d.email.configured
    ? { state: "ok", headline: text.email.ok, detail: text.email.okDetail }
    : { state: "warning", headline: text.email.notSet, detail: text.email.notSetDetail };

  let scoringTile;
  if (!last) {
    scoringTile = { state: "warning", headline: text.scoring.none };
  } else {
    const state = last.status === "succeeded" ? "ok" : last.status === "partial" ? "warning" : "down";
    const when = formatDateTime(last.finishedAt ?? last.requestedAt);
    const took = last.durationSeconds == null ? null : formatDuration(last.durationSeconds);
    scoringTile = {
      state,
      headline: text.scoring[last.status] ?? last.status,
      detail: text.scoring.detail(when, took),
      reason: last.failureReason ? text.scoring.reason(last.failureReason) : null,
    };
  }
  if (!scoring.enabled && scoringTile.state === "ok") {
    scoringTile = { ...scoringTile, state: "warning", headline: text.scoring.off };
  }
  if (scoring.nextScheduledRunAt) scoringTile.next = text.scoring.next(formatDateTime(scoring.nextScheduledRunAt));

  return [
    {
      key: "api",
      title: text.api.title,
      state: d.api.up ? "ok" : "down",
      headline: text.api.up,
      detail: text.api.detail(d.api.version, formatDuration(d.api.uptimeSeconds)),
    },
    { key: "database", title: text.database.title, ...database },
    { key: "ml", title: text.ml.title, ...mlTile },
    { key: "email", title: text.email.title, ...email },
    { key: "scoring", title: text.scoring.title, ...scoringTile },
  ];
}

/** One status tile: the state is spelled out in words and shown with an icon, not by colour. */
function Tile({ tile }) {
  const Icon = STATE_ICONS[tile.state];

  return (
    <li>
      <GlassCard variant="strong" as="article" className="flex h-full flex-col gap-1.5 p-4">
        <h3 className="text-sm text-muted-foreground">{tile.title}</h3>
        <p
          className={cn(
            "flex items-center gap-1.5 text-sm font-semibold",
            tile.state === "down" && "text-risk-flagged-foreground"
          )}
        >
          <Icon className="size-4 shrink-0" aria-hidden="true" />
          {text.states[tile.state]}
        </p>
        <p className="font-medium">{tile.headline}</p>
        {tile.detail && <p className="text-sm text-muted-foreground">{tile.detail}</p>}
        {tile.reason && <p className="text-sm break-words text-muted-foreground">{tile.reason}</p>}
        {tile.next && <p className="text-sm text-muted-foreground">{tile.next}</p>}
      </GlassCard>
    </li>
  );
}

function UsersChart({ users }) {
  const t = text.charts.users;
  const slices = users.map((row) => ({
    key: row.role,
    label: copy.roles[row.role] ?? row.role,
    value: row.active + row.disabled,
  }));
  const total = slices.reduce((sum, slice) => sum + slice.value, 0);
  const active = users.reduce((sum, row) => sum + row.active, 0);
  const disabled = users.reduce((sum, row) => sum + row.disabled, 0);

  return (
    <SectionCard title={t.title} variant="strong">
      <DonutChart
        slices={slices}
        label={t.label(total)}
        centre={<span className="text-2xl font-semibold tabular-nums">{formatNumber(total)}</span>}
      />
      <p className="mt-3 text-sm text-muted-foreground">{t.summary(active, disabled)}</p>
      <ChartTable
        caption={t.caption}
        columns={[
          { key: "role", header: t.columns.role },
          { key: "active", header: t.columns.active },
          { key: "disabled", header: t.columns.disabled },
        ]}
        rows={users.map((row) => ({ role: copy.roles[row.role] ?? row.role, active: row.active, disabled: row.disabled }))}
      />
    </SectionCard>
  );
}

function DailyBars({ chart, rows }) {
  const series = [
    { key: "succeeded", label: chart.succeeded, fill: "var(--foreground)" },
    { key: "failed", label: chart.failed, fill: "var(--destructive)" },
  ];

  return (
    <SectionCard title={chart.title} variant="strong">
      <BarChart
        data={rows}
        xKey="date"
        yKey="succeeded"
        series={series}
        label={chart.label}
        formatX={formatShortDate}
        formatY={(value) => formatNumber(value)}
        allowDecimals={false}
        height={200}
      />
      <ChartTable
        caption={chart.caption}
        columns={[
          { key: "date", header: chart.columns.day },
          { key: "succeeded", header: chart.columns.succeeded },
          { key: "failed", header: chart.columns.failed },
        ]}
        rows={rows}
      />
    </SectionCard>
  );
}

/**
 * The Admin's System health: status tiles for the API, database, ML service,
 * email and scoring, then three charts. It reads GET /api/admin/system-status
 * every 30 seconds, and not while the tab is hidden.
 * @param {{ onOpenRequests: () => void }} props
 */
export function SystemHealth({ onOpenRequests }) {
  const { data, error, refresh } = usePolling(getSystemStatus, POLL_MS);

  if (!data && error) return <ErrorState error={error} onRetry={refresh} />;

  if (!data) {
    return (
      <div role="status" aria-busy="true" className="flex flex-col gap-6">
        <ListSkeleton rows={3} />
        <ChartSkeleton />
      </div>
    );
  }

  const tiles = buildTiles(data);
  const mlDown = !data.mlService.reachable;
  const pending = data.accessRequests.find((row) => row.status === "pending")?.count ?? 0;

  return (
    <div className="flex flex-col gap-6">
      <p className="max-w-2xl text-sm text-muted-foreground">{text.updated(formatDateTime(data.generatedAt))}</p>
      {error && <p className="text-sm text-muted-foreground">{text.stale}</p>}

      {mlDown && (
        <p role="alert" className="flex items-start gap-2 rounded-xl border border-risk-flagged-foreground/40 bg-risk-flagged p-4 text-sm text-risk-flagged-foreground">
          <TriangleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          {text.mlDown}
        </p>
      )}

      <ul aria-label={text.tilesLabel} className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-5">
        {tiles.map((tile) => (
          <Tile key={tile.key} tile={tile} />
        ))}
      </ul>

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-3">
        <UsersChart users={data.usersByRole} />
        <DailyBars chart={text.charts.signIns} rows={data.signInsPerDay} />
        <DailyBars chart={text.charts.runs} rows={data.scoringRunsPerDay} />
      </div>

      <SectionCard title={text.charts.requests.title} variant="strong">
        <ul className="flex flex-wrap gap-x-6 gap-y-2 text-sm">
          {data.accessRequests.map((row) => (
            <li key={row.status} className="flex items-baseline gap-2">
              <span>{copy.admin.requests.status[row.status] ?? row.status}</span>
              <span className="font-semibold tabular-nums">{formatNumber(row.count)}</span>
            </li>
          ))}
        </ul>
        {pending > 0 && (
          <div className="mt-3">
            <Button type="button" variant="outline" className="h-10 px-4" onClick={onOpenRequests}>
              {text.charts.requests.link}
            </Button>
          </div>
        )}
      </SectionCard>
    </div>
  );
}
