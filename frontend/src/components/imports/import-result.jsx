"use client";

import { forwardRef } from "react";
import { CircleAlert, CircleCheck, TriangleAlert } from "lucide-react";
import { DataTable } from "@/components/data/data-table";
import { SectionCard } from "@/components/data/section-card";
import { GlassCard } from "@/components/glass/glass-card";
import { formatDate, formatNumber } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.imports;

function Stat({ label, value }) {
  return (
    <div className="flex flex-col gap-1 rounded-xl border border-border px-4 py-3">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="text-2xl font-semibold tracking-tight tabular-nums">{formatNumber(value)}</dd>
    </div>
  );
}

const errorColumns = [
  { key: "row", header: text.errors.row, className: "w-20 tabular-nums", cell: (item) => item.row ?? copy.common.noValue },
  { key: "column", header: text.errors.column, className: "w-44", cell: (item) => item.column ?? copy.common.noValue },
  {
    key: "message",
    header: text.errors.message,
    className: "min-w-80 whitespace-normal py-3",
    cell: (item) => item.message,
  },
];

function Errors({ result }) {
  const shown = result.errors.length;

  return (
    <SectionCard title={text.errors.title} variant="strong" flush className="overflow-hidden">
      <p className="px-5 pb-3 text-sm text-muted-foreground">
        {result.errorCount > shown ? text.errors.shown(shown, result.errorCount) : text.errors.all(result.errorCount)}
      </p>
      <DataTable
        caption={text.errors.caption}
        columns={errorColumns}
        rows={result.errors}
        getRowKey={(item) => `${item.row}-${item.column}-${item.message}`}
        emptyMessage={text.errors.none}
      />
    </SectionCard>
  );
}

function Warnings({ result }) {
  const shown = result.warnings.length;

  return (
    <GlassCard variant="strong" className="flex flex-col gap-3 p-5">
      <h3 className="flex items-center gap-2 text-base font-semibold">
        <TriangleAlert className="size-4" aria-hidden="true" />
        {text.warnings.title}
      </h3>
      {result.warningCount > shown && (
        <p className="text-sm text-muted-foreground">{text.warnings.shown(shown, result.warningCount)}</p>
      )}
      <ul className="flex flex-col gap-2 text-sm">
        {result.warnings.map((warning, index) => (
          <li key={index} className="flex flex-col gap-0.5 sm:flex-row sm:gap-3">
            <span className="shrink-0 font-medium tabular-nums sm:w-44">
              {warning.row == null ? text.warnings.fileWide : text.warnings.rowOf(warning.row, warning.column)}
            </span>
            <span className="text-muted-foreground">{warning.message}</span>
          </li>
        ))}
      </ul>
    </GlassCard>
  );
}

function Coverage({ coverage }) {
  return (
    <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
      <h3 className="text-base font-semibold">{text.coverage.title}</h3>
      <dl className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Stat label={text.coverage.sims} value={coverage.sims} />
        <Stat label={text.coverage.scorable} value={coverage.scorable} />
        <Stat label={text.coverage.notEnoughHistory} value={coverage.notEnoughHistory} />
        <Stat label={text.coverage.withGaps} value={coverage.withGaps} />
      </dl>
      <p className="text-sm text-muted-foreground">
        {coverage.firstDate && coverage.lastDate
          ? `${text.coverage.range(formatDate(coverage.firstDate), formatDate(coverage.lastDate))}. `
          : ""}
        {text.coverage.minimum(coverage.minimumDays)}
      </p>
    </GlassCard>
  );
}

/**
 * What the API said about a file: totals, the first row errors, warnings
 * and, for usage, coverage. Used for a preview, a finished import and a
 * rejected commit (422), so all three look alike. Takes focus when it
 * appears (the ref goes to the heading).
 * @param {{ result: any, kind: "preview" | "done" | "rejected" }} props
 */
export const ImportResult = forwardRef(function ImportResult({ result, kind }, headingRef) {
  const done = kind === "done";
  const rejected = kind === "rejected";
  const Icon = rejected ? CircleAlert : CircleCheck;
  const title = rejected ? text.rejectedTitle : done ? text.resultTitle : text.previewTitle;
  const note = rejected ? text.rejectedNote : done ? text.importedSummary(result.inserted + result.updated + result.unchanged) : text.previewNote;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-1" role="status">
        <h2 ref={headingRef} tabIndex={-1} className="flex items-center gap-2 text-lg font-semibold outline-none">
          <Icon className="size-5" aria-hidden="true" />
          {title}
        </h2>
        <p className="text-sm text-muted-foreground">{note}</p>
        {done && result.type === "usage" && <p className="text-sm font-medium">{text.scoringStarts}</p>}
      </div>

      <dl aria-label={text.totals.label} className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
        <Stat label={text.totals.rows} value={result.totalRows} />
        <Stat label={text.totals.valid} value={result.validRows} />
        <Stat label={text.totals.invalid} value={result.invalidRows} />
        <Stat label={done ? text.totals.inserted : text.totals.insert} value={result.inserted} />
        <Stat label={done ? text.totals.updated : text.totals.update} value={result.updated} />
        <Stat label={text.totals.unchanged} value={result.unchanged} />
      </dl>

      {result.coverage && <Coverage coverage={result.coverage} />}
      {result.errorCount > 0 && <Errors result={result} />}
      {result.warningCount > 0 && <Warnings result={result} />}
    </div>
  );
});
