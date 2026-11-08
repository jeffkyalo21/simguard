"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { RefreshCw } from "lucide-react";
import { BusyButton } from "@/components/shared/busy-button";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { usePermission } from "@/hooks/use-permission";
import { usePolling } from "@/hooks/use-polling";
import { errorMessage } from "@/lib/api";
import { getScoringStatus, runScoring } from "@/lib/data";
import { bumpDataVersion } from "@/lib/data-version";
import { formatDate } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.dashboard.scores;
// While a run is queued or running the status is read this often; otherwise once a minute.
const FAST_MS = 3000;
const SLOW_MS = 60_000;
const ACTIVE = new Set(["queued", "running"]);

/**
 * The latest scored day and, for the Security Officer, a "Refresh scores"
 * button. While a run is queued or running the button is disabled with the
 * reason and the state is shown in words. The Auditor sees the status only.
 * When a run finishes the dashboard numbers reload.
 */
export function ScoreRefresh() {
  const permission = usePermission("scoring.run");
  const [busy, setBusy] = useState(false);
  const [fast, setFast] = useState(false);
  const { data, refresh } = usePolling(getScoringStatus, fast ? FAST_MS : SLOW_MS);

  const running = Boolean(data?.runs.some((run) => ACTIVE.has(run.status)));
  const latest = data?.totals.latestScoredDate;
  const lastRun = data?.runs[0];
  const lastFailed = Boolean(lastRun && lastRun.status === "failed");

  // Poll quickly while a run is active (adjusted while rendering, not in an effect).
  if (running !== fast) setFast(running);

  // Reload the dashboard numbers once a run ends.
  const wasRunning = useRef(false);
  useEffect(() => {
    if (wasRunning.current && !running) {
      bumpDataVersion();
      toast.success(text.finished);
    }
    wasRunning.current = running;
  }, [running]);

  const start = useCallback(async () => {
    setBusy(true);
    try {
      const result = await runScoring();
      toast.success(result.merged ? text.merged : text.started);
      await refresh();
    } catch (error) {
      toast.error(errorMessage(error));
    } finally {
      setBusy(false);
    }
  }, [refresh]);

  if (!data) return null;

  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
      <p className="text-sm text-muted-foreground" aria-live="polite">
        {running ? text.running : latest ? text.latest(formatDate(latest)) : text.none}
        {!running && lastFailed && <span className="block">{text.failed}</span>}
      </p>
      {permission.allowed && (
        <DisabledReason reason={text.runningReason} disabled={running}>
          <BusyButton
            type="button"
            variant="outline"
            className="h-10 px-4"
            busy={busy || running}
            disabled={running}
            onClick={start}
          >
            {!busy && !running && <RefreshCw aria-hidden="true" />}
            {running ? text.refreshing : text.refresh}
          </BusyButton>
        </DisabledReason>
      )}
    </div>
  );
}
