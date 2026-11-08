"use client";

import { useState } from "react";
import { toast } from "sonner";
import { CircleCheck, CircleX } from "lucide-react";
import { BusyButton } from "@/components/shared/busy-button";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { useReturnFocus } from "@/hooks/use-return-focus";
import { ApiError, errorMessage } from "@/lib/api";
import { saveCarrierFeed, syncCarrierFeed, testCarrierFeed } from "@/lib/data";
import { formatDate, formatDateTime } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.settings.carriers.feed;
const IDS = { url: "carrier-feed-url", key: "carrier-feed-key", start: "carrier-feed-start", enabled: "carrier-feed-enabled" };

/** The first message the backend put on any field, or its general sentence. */
function problemText(error) {
  if (error instanceof ApiError) {
    const first = Object.values(error.errors ?? {}).flat()[0];
    if (first) return first;
  }
  return errorMessage(error);
}

function looksLikeUrl(value) {
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:";
  } catch {
    return false;
  }
}

/** Last sync: outcome in words, the day reached, when it ran, and why it failed. */
function LastSync({ carrier }) {
  const status = carrier.lastSyncStatus;
  const Icon = status === "failed" ? CircleX : CircleCheck;

  return (
    <section aria-labelledby="carrier-feed-last" className="flex flex-col gap-1 rounded-lg border border-input p-3 text-sm">
      <h3 id="carrier-feed-last" className="font-medium">
        {text.lastSync}
      </h3>
      <p aria-live="polite" className="flex flex-col gap-1">
        {!status ? (
          <span className="text-muted-foreground">{text.lastSyncNever}</span>
        ) : (
          <>
            <span className="inline-flex items-center gap-1.5 font-medium">
              {status !== "running" && <Icon className="size-4" aria-hidden="true" />}
              {text.statuses[status] ?? status}
            </span>
            {carrier.lastSyncMessage && <span className="text-muted-foreground">{carrier.lastSyncMessage}</span>}
          </>
        )}
        {carrier.lastSyncDate && <span className="text-muted-foreground">{text.lastSyncDay(formatDate(carrier.lastSyncDate))}</span>}
        {carrier.lastSyncAt && <span className="text-muted-foreground">{text.lastSyncAt(formatDateTime(carrier.lastSyncAt))}</span>}
      </p>
    </section>
  );
}

/** Fields of one opening. Keyed by the caller, so each opening starts fresh. */
function FeedForm({ open, onOpenChange, carrier, editable }) {
  const returnFocus = useReturnFocus(open);
  const [url, setUrl] = useState(carrier.feedBaseUrl ?? "");
  const [startDate, setStartDate] = useState(carrier.feedStartDate ?? "");
  const [enabled, setEnabled] = useState(carrier.feedEnabled);
  const [replacing, setReplacing] = useState(!carrier.feedKeySet);
  const [apiKey, setApiKey] = useState("");
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState("");
  const [result, setResult] = useState(null);

  const running = carrier.lastSyncStatus === "running";
  const keyTyped = replacing && apiKey.trim() !== "";
  const dirty =
    url.trim() !== (carrier.feedBaseUrl ?? "") ||
    startDate !== (carrier.feedStartDate ?? "") ||
    enabled !== carrier.feedEnabled ||
    keyTyped;
  const syncReason = !carrier.feedKeySet || !carrier.feedBaseUrl ? text.syncNeedsSave : dirty ? text.syncUnsaved : "";

  function check({ forSave }) {
    const next = {};
    if (!url.trim()) next.url = text.errors.urlRequired;
    else if (!looksLikeUrl(url.trim())) next.url = text.errors.urlInvalid;
    if (forSave && enabled && !carrier.feedKeySet && !keyTyped) next.key = text.errors.keyRequired;
    setErrors(next);
    if (next.url) document.getElementById(IDS.url)?.focus();
    else if (next.key) document.getElementById(IDS.key)?.focus();
    return Object.keys(next).length === 0;
  }

  async function handleSave(event) {
    event.preventDefault();
    setResult(null);
    if (!check({ forSave: true })) return;

    setBusy("save");
    try {
      await saveCarrierFeed(carrier.id, { baseUrl: url, enabled, startDate, apiKey: keyTyped ? apiKey.trim() : "" });
      toast.success(text.saved(carrier.name));
      setApiKey("");
      setReplacing(false);
      setUrl(url.trim().replace(/\/+$/, ""));
    } catch (error) {
      const fieldErrors = error instanceof ApiError ? error.errors : {};
      const next = {};
      if (fieldErrors.baseUrl?.[0]) next.url = fieldErrors.baseUrl[0];
      if (fieldErrors.apiKey?.[0]) next.key = fieldErrors.apiKey[0];
      if (fieldErrors.startDate?.[0]) next.start = fieldErrors.startDate[0];
      if (Object.keys(next).length > 0) setErrors(next);
      else toast.error(errorMessage(error));
    } finally {
      setBusy("");
    }
  }

  async function handleTest() {
    setResult(null);
    if (!check({ forSave: false })) return;

    setBusy("test");
    try {
      const outcome = await testCarrierFeed(carrier.id, { baseUrl: url, apiKey: keyTyped ? apiKey.trim() : "" });
      setResult({ ok: outcome.ok, message: outcome.message });
    } catch (error) {
      setResult({ ok: false, message: problemText(error) });
    } finally {
      setBusy("");
    }
  }

  async function handleSync() {
    setResult(null);
    setBusy("sync");
    try {
      await syncCarrierFeed(carrier.id);
      toast.success(text.syncStarted);
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) toast.error(text.syncRunning);
      else toast.error(problemText(error));
    } finally {
      setBusy("");
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !busy && onOpenChange(next)}>
      <DialogContent className="sm:max-w-lg" onCloseAutoFocus={returnFocus}>
        <form onSubmit={handleSave} noValidate className="flex flex-col gap-5">
          <DialogHeader>
            <DialogTitle className="text-lg font-semibold">{text.title(carrier.name)}</DialogTitle>
            <DialogDescription>{editable ? text.intro : text.readOnly}</DialogDescription>
          </DialogHeader>

          {editable && (
            <div className="flex flex-col gap-4">
              <FormField id={IDS.url} label={text.url} hint={text.urlHint} error={errors.url}>
                {(field) => (
                  <Input
                    {...field}
                    type="url"
                    inputMode="url"
                    value={url}
                    onChange={(event) => setUrl(event.target.value)}
                    placeholder={text.urlPlaceholder}
                    autoComplete="off"
                    spellCheck={false}
                    className="h-10"
                  />
                )}
              </FormField>

              {replacing ? (
                <FormField id={IDS.key} label={text.key} hint={text.keyHint} error={errors.key}>
                  {(field) => (
                    <div className="flex gap-2">
                      <Input
                        {...field}
                        type="password"
                        value={apiKey}
                        onChange={(event) => setApiKey(event.target.value)}
                        autoComplete="off"
                        spellCheck={false}
                        className="h-10"
                      />
                      {carrier.feedKeySet && (
                        <Button
                          type="button"
                          variant="outline"
                          className="h-10 px-3"
                          onClick={() => {
                            setReplacing(false);
                            setApiKey("");
                          }}
                        >
                          {text.keyKeep}
                        </Button>
                      )}
                    </div>
                  )}
                </FormField>
              ) : (
                <div className="flex flex-col gap-2">
                  <span className="text-sm font-medium">{text.key}</span>
                  <div className="flex flex-wrap items-center gap-3">
                    <span className="inline-flex items-center gap-1.5 text-sm">
                      <CircleCheck className="size-4" aria-hidden="true" />
                      {text.keySaved}
                    </span>
                    <Button type="button" variant="outline" size="sm" className="h-9 px-3" onClick={() => setReplacing(true)}>
                      {text.keyReplace}
                    </Button>
                  </div>
                  <p className="text-sm text-muted-foreground">{text.keySavedHint}</p>
                </div>
              )}

              <FormField id={IDS.start} label={text.startDate} hint={text.startDateHint} error={errors.start}>
                {(field) => (
                  <Input {...field} type="date" value={startDate} onChange={(event) => setStartDate(event.target.value)} className="h-10 w-44" />
                )}
              </FormField>

              <div className="flex items-start gap-3">
                <input
                  id={IDS.enabled}
                  type="checkbox"
                  role="switch"
                  checked={enabled}
                  onChange={(event) => setEnabled(event.target.checked)}
                  aria-describedby={`${IDS.enabled}-hint`}
                  className="mt-0.5 size-5 shrink-0 cursor-pointer accent-foreground focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                />
                <div className="flex flex-col">
                  <label htmlFor={IDS.enabled} className="cursor-pointer text-sm font-medium">
                    {text.enabled}
                  </label>
                  <p id={`${IDS.enabled}-hint`} className="text-sm text-muted-foreground">
                    {text.enabledHint}
                  </p>
                </div>
              </div>

              <div aria-live="polite" className="min-h-6">
                {result && (
                  <p className="flex items-start gap-2 text-sm">
                    {result.ok ? <CircleCheck className="mt-0.5 size-4 shrink-0" aria-hidden="true" /> : <CircleX className="mt-0.5 size-4 shrink-0" aria-hidden="true" />}
                    <span>
                      <span className="font-medium">{result.ok ? text.testOk : text.testFailed}.</span> {result.message}
                    </span>
                  </p>
                )}
              </div>
            </div>
          )}

          <LastSync carrier={carrier} />

          <DialogFooter className="mx-0 mb-0 flex-wrap rounded-none border-0 bg-transparent p-0">
            <DialogClose asChild>
              <Button type="button" variant="outline" className="h-10 px-4" disabled={Boolean(busy)}>
                {copy.actions.close}
              </Button>
            </DialogClose>
            {editable && (
              <>
                <BusyButton type="button" variant="outline" className="h-10 px-4" busy={busy === "test"} disabled={Boolean(busy)} onClick={handleTest}>
                  {busy === "test" ? text.testing : text.test}
                </BusyButton>
                <DisabledReason reason={syncReason} disabled={Boolean(syncReason)}>
                  <BusyButton
                    type="button"
                    variant="outline"
                    className="h-10 px-4"
                    busy={busy === "sync" || running}
                    disabled={Boolean(syncReason) || Boolean(busy) || running}
                    onClick={handleSync}
                  >
                    {running ? text.syncing : text.sync}
                  </BusyButton>
                </DisabledReason>
                <BusyButton type="submit" busy={busy === "save"} disabled={Boolean(busy)} className="h-10 px-4">
                  {busy === "save" ? text.saving : text.save}
                </BusyButton>
              </>
            )}
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Usage feed of one carrier: address, key (write-only), start day, switch,
 * Test connection, Sync now and the last sync. `carrier` is the live row, so
 * the last sync updates while the dialog is open. Without `editable` it only
 * shows the last sync.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void, carrier: object | null, editable: boolean }} props
 */
export function CarrierFeedDialog({ open, onOpenChange, carrier, editable }) {
  const opening = useOpeningKey(open);
  if (!opening || !carrier) return null;

  return <FeedForm key={opening} open={open} onOpenChange={onOpenChange} carrier={carrier} editable={editable} />;
}
