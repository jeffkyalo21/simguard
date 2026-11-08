"use client";

import { useEffect, useRef, useState } from "react";
import { toast } from "sonner";
import { CircleAlert, Download, FileText, Upload, X } from "lucide-react";
import { ImportResult } from "@/components/imports/import-result";
import { GlassCard } from "@/components/glass/glass-card";
import { BusyButton } from "@/components/shared/busy-button";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { Button } from "@/components/ui/button";
import { usePermission } from "@/hooks/use-permission";
import { ApiError, errorMessage } from "@/lib/api";
import { IMPORT_MAX_BYTES, commitImport, importTemplateUrl, previewImport } from "@/lib/data";
import { formatBytes } from "@/lib/format";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.imports;

/** Client side checks, so an obvious mistake never uploads 25 MB. */
function checkFile(file) {
  if (!file.name.toLowerCase().endsWith(".csv")) return text.file.notCsv;
  if (file.size === 0) return text.file.empty;
  if (file.size > IMPORT_MAX_BYTES) return text.file.tooLarge;
  return null;
}

/** The line for a failed upload. A 400 carries the file problem itself. */
function problemFor(error) {
  if (error instanceof ApiError) {
    if (error.status === 400 && error.detail) return error.detail;
    if (error.status === 413) return text.file.tooLarge;
  }
  return errorMessage(error, { 422: text.failed });
}

function DropZone({ file, error, disabled, onFile, onClear }) {
  const inputRef = useRef(null);
  const [over, setOver] = useState(false);
  const errorId = "import-file-error";

  function take(files) {
    const next = files?.[0];
    if (next) onFile(next);
  }

  return (
    <div className="flex flex-col gap-2">
      <div
        onDragOver={(event) => {
          if (disabled) return;
          event.preventDefault();
          setOver(true);
        }}
        onDragLeave={() => setOver(false)}
        onDrop={(event) => {
          event.preventDefault();
          setOver(false);
          if (!disabled) take(event.dataTransfer.files);
        }}
        className={cn(
          "flex flex-col items-center justify-center gap-3 rounded-2xl border-2 border-dashed px-6 py-10 text-center transition-colors",
          over ? "border-foreground bg-foreground/5" : "border-input",
          error && "border-risk-flagged-foreground"
        )}
      >
        <input
          ref={inputRef}
          id="import-file"
          type="file"
          accept=".csv,text/csv"
          className="sr-only"
          tabIndex={-1}
          aria-label={text.file.label}
          aria-describedby={error ? errorId : undefined}
          disabled={disabled}
          onChange={(event) => {
            take(event.target.files);
            // Lets the same file be picked again after it was removed.
            event.target.value = "";
          }}
        />
        {file ? (
          <>
            <FileText className="size-8 text-muted-foreground" aria-hidden="true" />
            <p className="text-sm font-medium break-all">{text.file.selected(file.name, formatBytes(file.size))}</p>
            <div className="flex flex-wrap justify-center gap-2">
              <Button type="button" variant="outline" className="touch-target h-10 px-4" disabled={disabled} onClick={() => inputRef.current?.click()}>
                {text.file.change}
              </Button>
              <Button type="button" variant="ghost" className="touch-target h-10 px-4" disabled={disabled} onClick={onClear}>
                <X aria-hidden="true" />
                {text.file.remove}
              </Button>
            </div>
          </>
        ) : (
          <>
            <Upload className="size-8 text-muted-foreground" aria-hidden="true" />
            <p className="text-sm text-muted-foreground">{over ? text.file.dropActive : text.file.drop}</p>
            <Button type="button" variant="outline" className="touch-target h-10 px-4" disabled={disabled} onClick={() => inputRef.current?.click()}>
              {text.file.choose}
            </Button>
            <p className="text-sm text-muted-foreground">{text.file.limits}</p>
          </>
        )}
      </div>
      {error && (
        <p id={errorId} role="alert" className="text-sm text-risk-flagged-foreground">
          {error}
        </p>
      )}
    </div>
  );
}

/**
 * One import: pick a file, preview it, confirm. The parent keys this by
 * type, so changing the type starts over. Phases: idle, previewing,
 * previewed, importing, done, rejected.
 */
export function ImportPanel({ type }) {
  const importing = usePermission("imports.run");
  const [file, setFile] = useState(null);
  const [fileError, setFileError] = useState(null);
  const [skipInvalid, setSkipInvalid] = useState(false);
  const [phase, setPhase] = useState("idle");
  const [result, setResult] = useState(null);
  const [problem, setProblem] = useState(null);
  const headingRef = useRef(null);

  const busy = phase === "previewing" || phase === "importing";
  const hasResult = result !== null;

  // The result heading takes focus when a result or a problem appears.
  useEffect(() => {
    if (result) headingRef.current?.focus();
  }, [result]);

  function reset() {
    setResult(null);
    setProblem(null);
    setPhase("idle");
  }

  function choose(next) {
    const error = checkFile(next);
    reset();
    setFileError(error);
    setFile(error ? null : next);
  }

  function clear() {
    reset();
    setFileError(null);
    setFile(null);
  }

  async function runPreview() {
    setPhase("previewing");
    setProblem(null);
    setResult(null);
    try {
      setResult(await previewImport(type, file));
      setPhase("previewed");
    } catch (error) {
      setProblem(problemFor(error));
      setPhase("idle");
    }
  }

  async function runCommit() {
    setPhase("importing");
    setProblem(null);
    try {
      const committed = await commitImport(type, file, { skipInvalid });
      setResult(committed);
      if (committed.status === "rejected") {
        setPhase("rejected");
      } else {
        setPhase("done");
        toast.success(text.importedSummary(committed.inserted + committed.updated + committed.unchanged));
      }
    } catch (error) {
      setProblem(problemFor(error));
      setPhase("previewed");
    }
  }

  const finished = phase === "done" || phase === "rejected";
  const hasErrors = (result?.errorCount ?? 0) > 0;
  const noneValid = (result?.validRows ?? 0) === 0;
  const blocked = hasErrors && !skipInvalid;
  const confirmReason = !importing.allowed
    ? importing.reason
    : noneValid
      ? text.nothingValid
      : blocked
        ? text.blocked
        : null;

  return (
    <div className="flex flex-col gap-6">
      <GlassCard variant="strong" className="flex flex-col gap-5 p-5">
        <fieldset disabled={busy} className="flex flex-col gap-5">
          <legend className="sr-only">{text.file.label}</legend>
          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-sm text-muted-foreground">{text.hints[type]}</p>
            <Button asChild variant="outline" className="touch-target h-10 px-4">
              <a href={importTemplateUrl(type)} download={`${type}-template.csv`}>
                <Download aria-hidden="true" />
                {text.template}
                <span className="sr-only">{`: ${text.types[type]}`}</span>
              </a>
            </Button>
          </div>

          <DropZone file={file} error={fileError} disabled={busy || finished} onFile={choose} onClear={clear} />

          {file && !finished && (
            <div className="flex flex-wrap items-center gap-3">
              <DisabledReason reason={importing.reason ?? ""} disabled={!importing.allowed}>
                <BusyButton
                  type="button"
                  className="touch-target h-10 px-5"
                  busy={phase === "previewing"}
                  disabled={!importing.allowed || phase === "importing"}
                  onClick={runPreview}
                >
                  {phase === "previewing" ? text.previewing : text.preview}
                </BusyButton>
              </DisabledReason>
            </div>
          )}
        </fieldset>

        {problem && (
          <div role="alert" className="flex items-start gap-2 rounded-xl border border-risk-flagged-foreground px-4 py-3 text-sm">
            <CircleAlert className="mt-0.5 size-4 shrink-0 text-risk-flagged-foreground" aria-hidden="true" />
            <div className="flex flex-col gap-0.5">
              <p className="font-medium">{text.fileProblem}</p>
              <p className="text-muted-foreground">{problem}</p>
            </div>
          </div>
        )}
      </GlassCard>

      {hasResult && (
        <ImportResult
          ref={headingRef}
          result={result}
          kind={phase === "done" ? "done" : phase === "rejected" ? "rejected" : "preview"}
        />
      )}

      {phase !== "idle" && phase !== "previewing" && !finished && (
        <GlassCard variant="strong" className="flex flex-col gap-4 p-5">
          <fieldset disabled={busy} className="flex flex-col gap-4">
            <legend className="sr-only">{text.confirm}</legend>
            {hasErrors && (
              <label className="flex min-h-10 cursor-pointer items-start gap-3 text-sm">
                <input
                  type="checkbox"
                  className="mt-0.5 size-5 shrink-0 accent-foreground"
                  checked={skipInvalid}
                  onChange={(event) => setSkipInvalid(event.target.checked)}
                />
                <span className="flex flex-col">
                  <span className="font-medium">{text.skipInvalid}</span>
                  <span className="text-muted-foreground">{text.skipInvalidHint}</span>
                </span>
              </label>
            )}
            <div className="flex flex-wrap items-center gap-3">
              <DisabledReason reason={confirmReason ?? ""} disabled={confirmReason !== null}>
                <BusyButton
                  type="button"
                  className="touch-target h-10 px-5"
                  busy={phase === "importing"}
                  disabled={confirmReason !== null}
                  onClick={runCommit}
                >
                  {phase === "importing" ? text.importing : text.confirm}
                </BusyButton>
              </DisabledReason>
              {confirmReason && <p className="text-sm text-muted-foreground">{confirmReason}</p>}
            </div>
          </fieldset>
        </GlassCard>
      )}

      {finished && (
        <div>
          <Button type="button" variant="outline" className="touch-target h-10 px-5" onClick={clear}>
            {text.startOver}
          </Button>
        </div>
      )}
    </div>
  );
}
