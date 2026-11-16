"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { MailCheck } from "lucide-react";
import { ErrorSummary } from "@/components/auth/error-summary";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { Input } from "@/components/ui/input";
import { authErrorText, authRequest } from "@/lib/auth/client";
import { isEmail } from "@/lib/validation";
import { copy } from "@/lib/copy";

const text = copy.login.forgot;

function validate(email) {
  if (!email) return text.errors.emailRequired;
  if (!isEmail(email)) return text.errors.emailInvalid;
  return null;
}

function BackToSignIn() {
  return (
    <Link href="/login" className="w-fit self-center rounded-sm text-sm text-muted-foreground underline underline-offset-4">
      {copy.auth.backToSignIn}
    </Link>
  );
}

// Asks for a reset email. The answer is the same for every address, so the
// confirmation never says whether the account exists.
export function ForgotPasswordForm() {
  const [error, setError] = useState(null);
  const [formError, setFormError] = useState(null);
  const [focusKey, setFocusKey] = useState(0);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState(false);
  const sentRef = useRef(null);

  useEffect(() => {
    if (sent) sentRef.current?.focus();
  }, [sent]);

  async function handleSubmit(event) {
    event.preventDefault();
    if (busy) return;

    const email = String(new FormData(event.currentTarget).get("email") ?? "").trim();
    const problem = validate(email);

    if (problem) {
      setError(problem);
      setFormError(null);
      setFocusKey((key) => key + 1);
      return;
    }

    setBusy(true);
    setError(null);
    const result = await authRequest("/api/auth/forgot-password", { email });
    setBusy(false);

    if (!result.ok) {
      setFormError(result.error);
      setFocusKey((key) => key + 1);
      return;
    }

    setFormError(null);
    setSent(true);
  }

  if (sent) {
    return (
      <div className="flex flex-col gap-4">
        <div
          ref={sentRef}
          tabIndex={-1}
          role="status"
          className="flex gap-3 rounded-xl border border-glass-border bg-background/60 p-4 text-sm outline-none"
        >
          <MailCheck className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <div className="flex flex-col gap-1">
            <p className="font-medium">{text.sentTitle}</p>
            <p>{text.sent}</p>
            <p className="text-muted-foreground">{text.sentHelp}</p>
          </div>
        </div>
        <button
          type="button"
          onClick={() => setSent(false)}
          className="w-fit rounded-sm text-sm font-medium underline underline-offset-4"
        >
          {text.askAgain}
        </button>
        <BackToSignIn />
      </div>
    );
  }

  const items = [
    ...(error ? [{ text: error, fieldId: "email" }] : []),
    ...(formError ? [{ text: authErrorText(formError) }] : []),
  ];

  return (
    <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-4" aria-busy={busy || undefined}>
      <ErrorSummary items={items} focusKey={focusKey} />
      <FormField id="email" label={copy.login.email} error={error ?? undefined}>
        {(field) => (
          <Input
            {...field}
            name="email"
            type="email"
            inputMode="email"
            autoComplete="username"
            autoCapitalize="none"
            spellCheck={false}
            placeholder={copy.login.emailPlaceholder}
            className="h-10"
          />
        )}
      </FormField>
      <BusyButton type="submit" busy={busy} size="lg" className="mt-2 h-10 w-full">
        {text.submit}
      </BusyButton>
      <BackToSignIn />
    </form>
  );
}
