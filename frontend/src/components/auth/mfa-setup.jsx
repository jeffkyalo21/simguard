"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Copy, Download, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { ErrorSummary } from "@/components/auth/error-summary";
import { QrCode } from "@/components/auth/qr-code";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { authErrorText, authRequest, destinationAfterSignIn, loginHref } from "@/lib/auth/client";
import { ERROR_CODES } from "@/lib/auth/core";
import { copy } from "@/lib/copy";

const text = copy.auth.setup;

async function copyText(value, done) {
  try {
    await navigator.clipboard.writeText(value);
    toast.success(done);
  } catch {
    toast.error(text.copyFailed);
  }
}

function downloadCodes(codes) {
  const body = `${text.downloadHeading}\n\n${codes.join("\n")}\n`;
  const url = URL.createObjectURL(new Blob([body], { type: "text/plain;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = text.downloadName;
  document.body.append(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** Shown once after the first code is confirmed. */
function RecoveryCodes({ codes, onContinue }) {
  const headingRef = useRef(null);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState(null);
  const [focusKey, setFocusKey] = useState(0);

  useEffect(() => {
    headingRef.current?.focus();
  }, []);

  function handleContinue(event) {
    event.preventDefault();
    if (!saved) {
      setError(text.savedRequired);
      setFocusKey((key) => key + 1);
      return;
    }
    onContinue();
  }

  return (
    <form onSubmit={handleContinue} noValidate className="flex flex-col gap-4">
      <h2 ref={headingRef} tabIndex={-1} className="text-lg font-semibold">
        {text.codesTitle}
      </h2>
      <p className="text-sm text-muted-foreground">{text.codesIntro}</p>
      <ErrorSummary items={error ? [{ text: error, fieldId: "codes-saved" }] : []} focusKey={focusKey} />
      <ol
        aria-label={text.codesLabel}
        className="grid grid-cols-2 gap-x-6 gap-y-1.5 rounded-xl border border-glass-border bg-background/60 p-4 font-mono text-sm"
      >
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ol>
      <div className="flex flex-wrap gap-2">
        <Button type="button" variant="outline" className="h-10" onClick={() => copyText(codes.join("\n"), text.codesCopied)}>
          <Copy aria-hidden="true" />
          {text.copyCodes}
        </Button>
        <Button type="button" variant="outline" className="h-10" onClick={() => downloadCodes(codes)}>
          <Download aria-hidden="true" />
          {text.downloadCodes}
        </Button>
      </div>
      <label htmlFor="codes-saved" className="flex min-h-11 cursor-pointer items-center gap-3 text-sm font-medium">
        <input
          id="codes-saved"
          type="checkbox"
          checked={saved}
          onChange={(event) => {
            setSaved(event.target.checked);
            if (event.target.checked) setError(null);
          }}
          aria-invalid={error ? true : undefined}
          className="size-5 accent-[var(--primary)]"
        />
        {text.saved}
      </label>
      <Button type="submit" size="lg" className="h-10 w-full">
        {copy.auth.continue}
      </Button>
    </form>
  );
}

/**
 * First sign-in: shows the QR code and setup key, confirms the first code,
 * then shows the recovery codes once.
 */
export function MfaSetup() {
  const router = useRouter();
  const next = useSearchParams().get("next");
  const [setup, setSetup] = useState({ status: "loading" });
  const [done, setDone] = useState(null);
  const [fieldError, setFieldError] = useState(null);
  const [formError, setFormError] = useState(null);
  const [focusKey, setFocusKey] = useState(0);
  const [busy, setBusy] = useState(false);
  const started = useRef(false);

  const restart = useCallback(() => router.replace(loginHref({ next, reason: "restart" })), [router, next]);

  const start = useCallback(async () => {
    setSetup({ status: "loading" });
    const result = await authRequest("/api/auth/mfa/enroll/start");
    if (result.ok) return setSetup({ status: "ready", ...result.data });
    if (result.error.code === ERROR_CODES.challengeExpired) return restart();
    setSetup({ status: "error", error: result.error });
  }, [restart]);

  // Each start call replaces the key, so it runs once per visit (the ref
  // survives React's development double effect).
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    start();
  }, [start]);

  async function handleConfirm(event) {
    event.preventDefault();
    const code = String(new FormData(event.currentTarget).get("code") ?? "").replace(/\s+/g, "");
    let invalid = null;
    if (!code) invalid = copy.auth.mfa.errors.codeRequired;
    else if (!/^\d{6}$/.test(code)) invalid = copy.auth.mfa.errors.codeFormat;

    if (invalid) {
      setFieldError(invalid);
      setFormError(null);
      setFocusKey((key) => key + 1);
      return;
    }

    setBusy(true);
    const result = await authRequest("/api/auth/mfa/enroll/confirm", { code });
    setBusy(false);

    if (!result.ok) {
      if (result.error.code === ERROR_CODES.challengeExpired) return restart();
      setFieldError(null);
      setFormError(result.error);
      setFocusKey((key) => key + 1);
      return;
    }

    setDone(result.data);
  }

  if (done) {
    return (
      <RecoveryCodes
        codes={done.recoveryCodes}
        onContinue={() => router.replace(destinationAfterSignIn({ ...done, next }))}
      />
    );
  }

  if (setup.status === "loading") {
    return (
      <p role="status" className="flex items-center gap-2 text-sm text-muted-foreground">
        <Loader2 className="size-4 animate-spin" aria-hidden="true" />
        {text.loading}
      </p>
    );
  }

  if (setup.status === "error") {
    return (
      <div className="flex flex-col gap-4">
        <ErrorSummary items={[{ text: `${text.loadFailed} ${authErrorText(setup.error)}` }]} focusKey={0} />
        <Button type="button" className="h-10" onClick={start}>
          {text.retry}
        </Button>
      </div>
    );
  }

  const items = [
    ...(fieldError ? [{ text: fieldError, fieldId: "code" }] : []),
    ...(formError ? [{ text: authErrorText(formError) }] : []),
  ];
  const hint = formError?.code === ERROR_CODES.mfaInvalid ? copy.auth.errors.lockoutHint : undefined;

  return (
    <div className="flex flex-col gap-5">
      <section aria-labelledby="setup-scan" className="flex flex-col gap-3">
        <h2 id="setup-scan" className="text-sm font-medium">
          {text.stepScan}
        </h2>
        <div className="self-center rounded-xl bg-white p-2">
          <QrCode value={setup.authenticatorUri} label={text.qrLabel} />
        </div>
        <p className="text-sm text-muted-foreground">{text.stepManual}</p>
        <div className="flex items-center gap-2">
          <code
            aria-label={text.manualKey}
            className="min-w-0 flex-1 rounded-lg border border-glass-border bg-background/60 px-3 py-2 font-mono text-sm break-all"
          >
            {setup.sharedKey}
          </code>
          <Button
            type="button"
            variant="outline"
            className="h-10"
            onClick={() => copyText(setup.sharedKey.replace(/\s+/g, ""), text.keyCopied)}
          >
            <Copy aria-hidden="true" />
            {text.copyKey}
          </Button>
        </div>
      </section>

      <form onSubmit={handleConfirm} noValidate aria-labelledby="setup-confirm" className="flex flex-col gap-4" aria-busy={busy || undefined}>
        <h2 id="setup-confirm" className="text-sm font-medium">
          {text.stepConfirm}
        </h2>
        <ErrorSummary items={items} hint={hint} focusKey={focusKey} />
        <FormField id="code" label={copy.auth.mfa.code} error={fieldError}>
          {(field) => (
            <Input
              {...field}
              name="code"
              type="text"
              inputMode="numeric"
              autoComplete="one-time-code"
              maxLength={8}
              className="h-10 font-mono tracking-widest"
            />
          )}
        </FormField>
        <BusyButton type="submit" busy={busy} size="lg" className="h-10 w-full">
          {text.confirm}
        </BusyButton>
      </form>
    </div>
  );
}
