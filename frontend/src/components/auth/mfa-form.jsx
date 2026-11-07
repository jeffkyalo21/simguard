"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ErrorSummary } from "@/components/auth/error-summary";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { Input } from "@/components/ui/input";
import { authErrorText, authRequest, destinationAfterSignIn, loginHref } from "@/lib/auth/client";
import { ERROR_CODES, safeNextPath } from "@/lib/auth/core";
import { copy } from "@/lib/copy";

const text = copy.auth.mfa;

function methodHref(next, recovery) {
  const params = new URLSearchParams();
  const safe = safeNextPath(next);
  if (recovery) params.set("method", "recovery");
  if (safe) params.set("next", safe);
  const query = params.toString();
  return `/login/mfa${query ? `?${query}` : ""}`;
}

function validate(code, recovery) {
  if (recovery) return code ? null : text.errors.recoveryRequired;
  if (!code) return text.errors.codeRequired;
  return /^\d{6}$/.test(code) ? null : text.errors.codeFormat;
}

function CodeForm({ recovery, next }) {
  const router = useRouter();
  const [fieldError, setFieldError] = useState(null);
  const [formError, setFormError] = useState(null);
  const [focusKey, setFocusKey] = useState(0);
  const [busy, setBusy] = useState(false);

  function show(field, error) {
    setFieldError(field);
    setFormError(error);
    setFocusKey((key) => key + 1);
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const raw = String(new FormData(event.currentTarget).get("code") ?? "");
    const code = recovery ? raw.trim().toLowerCase() : raw.replace(/\s+/g, "");
    const invalid = validate(code, recovery);
    if (invalid) return show(invalid, null);

    setBusy(true);
    const result = await authRequest("/api/auth/mfa/verify", { code });

    if (!result.ok) {
      setBusy(false);
      if (result.error.code === ERROR_CODES.challengeExpired) {
        router.replace(loginHref({ next, reason: "restart" }));
        return;
      }
      return show(null, result.error);
    }

    router.replace(destinationAfterSignIn({ ...result.data, next }));
  }

  const items = [
    ...(fieldError ? [{ text: fieldError, fieldId: "code" }] : []),
    ...(formError ? [{ text: authErrorText(formError, { recovery }) }] : []),
  ];
  const hint = formError?.code === ERROR_CODES.mfaInvalid ? copy.auth.errors.lockoutHint : undefined;

  return (
    <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-4" aria-busy={busy || undefined}>
      <ErrorSummary items={items} hint={hint} focusKey={focusKey} />
      <FormField id="code" label={recovery ? text.recoveryCode : text.code} error={fieldError}>
        {(field) => (
          <Input
            {...field}
            aria-describedby={[field["aria-describedby"], "code-hint"].filter(Boolean).join(" ")}
            name="code"
            type="text"
            inputMode={recovery ? "text" : "numeric"}
            autoComplete={recovery ? "off" : "one-time-code"}
            autoCapitalize="none"
            spellCheck={false}
            maxLength={recovery ? 64 : 8}
            className="h-10 font-mono tracking-widest"
          />
        )}
      </FormField>
      <p id="code-hint" className="-mt-2 text-sm text-muted-foreground">
        {recovery ? text.recoveryHint : text.codeHint}
      </p>
      <BusyButton type="submit" busy={busy} size="lg" className="h-10 w-full">
        {copy.auth.verify}
      </BusyButton>
    </form>
  );
}

/**
 * Step 2 of a normal sign-in: the 6-digit code, or (with ?method=recovery)
 * one recovery code. Nothing submits until the button is pressed.
 */
export function MfaForm() {
  const params = useSearchParams();
  const next = params.get("next");
  const recovery = params.get("method") === "recovery";

  return (
    <div className="flex flex-col gap-6">
      <p className="text-sm text-muted-foreground">{recovery ? text.recoveryIntro : text.intro}</p>
      {/* key: switching method starts with an empty field and no errors. */}
      <CodeForm key={recovery ? "recovery" : "app"} recovery={recovery} next={next} />
      <div className="flex flex-col items-center gap-2 text-sm">
        <Link href={methodHref(next, !recovery)} replace className="link-accent rounded-sm">
          {recovery ? text.useApp : text.useRecovery}
        </Link>
        <Link href={loginHref({ next })} className="rounded-sm text-muted-foreground underline-offset-2 hover:underline">
          {copy.auth.backToSignIn}
        </Link>
      </div>
    </div>
  );
}
