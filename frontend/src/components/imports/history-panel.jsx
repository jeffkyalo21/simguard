"use client";

import { useState } from "react";
import { CircleAlert, CircleCheck, CircleX } from "lucide-react";
import { DataTable } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { SectionCard } from "@/components/data/section-card";
import { useResource } from "@/hooks/use-resource";
import { listImportRuns } from "@/lib/data";
import { formatDateTime } from "@/lib/format";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.imports.history;
const PAGE_SIZE = 20;

const STATUS_ICONS = { committed: CircleCheck, rejected: CircleAlert, failed: CircleX };

// Status is words plus an icon; it is not a risk level, so it stays neutral.
function RunStatus({ status }) {
  const Icon = STATUS_ICONS[status] ?? CircleAlert;
  const good = status === "committed";

  return (
    <span
      className={cn(
        "inline-flex h-6 items-center gap-1 rounded-full px-2.5 text-xs font-medium whitespace-nowrap",
        good ? "bg-foreground/8 text-foreground" : "border border-dashed border-input text-muted-foreground"
      )}
    >
      <Icon className="size-3.5" aria-hidden="true" />
      {text.statuses[status] ?? status}
    </span>
  );
}

const columns = [
  { key: "type", header: text.type, cell: (run) => copy.imports.types[run.type] ?? run.type },
  { key: "file", header: text.file, cell: (run) => <span className="block max-w-56 truncate" title={run.fileName}>{run.fileName}</span> },
  { key: "uploader", header: text.uploader, cell: (run) => run.uploadedByEmail ?? copy.common.noValue },
  { key: "time", header: text.time, cell: (run) => formatDateTime(run.startedAt) },
  {
    key: "counts",
    header: text.counts,
    cell: (run) => (
      <span className="flex flex-col">
        <span className="tabular-nums">{text.rows(run.totalRows, run.inserted, run.updated, run.unchanged)}</span>
        {(run.errorCount > 0 || run.skipInvalid) && (
          <span className="text-xs text-muted-foreground">
            {[run.errorCount > 0 && text.errorCount(run.errorCount), run.skipInvalid && text.skipped]
              .filter(Boolean)
              .join(", ")}
          </span>
        )}
      </span>
    ),
  },
  { key: "status", header: text.status, cell: (run) => <RunStatus status={run.status} /> },
];

/** Past commit attempts, newest first, 20 to a page. */
export function HistoryPanel() {
  const [page, setPage] = useState(1);
  const resource = useResource(() => listImportRuns({ page, pageSize: PAGE_SIZE }), [page]);
  const result = resource.data;

  return (
    <div className="flex flex-col gap-4">
      <SectionCard title={text.caption} variant="strong" flush className="overflow-hidden">
        <DataTable
          caption={text.caption}
          columns={columns}
          rows={result?.items ?? null}
          getRowKey={(run) => run.id}
          error={resource.error}
          onRetry={resource.retry}
          emptyMessage={text.empty}
        />
      </SectionCard>
      {result && result.total > 0 && (
        <Pagination page={result.page} pageSize={result.pageSize} total={result.total} onPageChange={setPage} />
      )}
    </div>
  );
}
