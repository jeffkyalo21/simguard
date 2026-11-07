"use client";

import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Info } from "lucide-react";
import { toast } from "sonner";
import { ErrorSummary } from "@/components/auth/error-summary";
import { PasswordInput } from "@/components/auth/password-input";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { authErrorText, authRequest, nextQuery } from "@/lib/auth/client";
import { ERROR_CODES } from "@/lib/auth/core";
import { isEmail } from "@/lib/validation";
import { copy } from "@/lib/copy";

function validate({ email, password }) {
  const errors = {};
  if (!email.trim()) errors.email = copy.login.errors.emailRequired;
  else if (!isEmail(email.trim())) errors.email = copy.login.errors.emailInvalid;
  if (!password) errors.password = copy.login.errors.passwordRequired;
  return errors;
}

/** Why the person landed here (?reason=expired, restart or signedOut). */
export function useReasonNotice() {
  const reason = useSearchParams().get("reason");
  return Object.hasOwn(copy.login.reasons, reason ?? "") ? copy.login.reasons[reason] : null;
}

export function ReasonNotice({ text }) {
  if (!text) return null;
  return (
    <p role="status" className="flex gap-2 rounded-xl border border-glass-border bg-background/60 p-3 text-sm">
      <Info className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      {text}
    </p>
  );
}

// Step 1 of sign-in. The route handler keeps the MFA token in an httpOnly
// cookie; this form only learns which step comes next.
export function LoginForm() {
  const router = useRouter();
  const next = useSearchParams().get("next");
  const notice = useReasonNotice();
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState(null);
  const [focusKey, setFocusKey] = useState(0);
  const [busy, setBusy] = useState(false);

  function showErrors(fieldErrors, error) {
    setErrors(fieldErrors);
    setFormError(error);
    setFocusKey((key) => key + 1);
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const values = { email: String(form.get("email") ?? ""), password: String(form.get("password") ?? "") };
    const fieldErrors = validate(values);

    if (Object.keys(fieldErrors).length > 0) return showErrors(fieldErrors, null);

    setBusy(true);
    const result = await authRequest("/api/auth/login", { email: values.email.trim(), password: values.password });
    setBusy(false);

    if (!result.ok) return showErrors({}, result.error);
    router.push(`/login/${result.data.next === "mfa-setup" ? "mfa-setup" : "mfa"}${nextQuery(next)}`);
  }

  const items = [
    ...(errors.email ? [{ text: errors.email, fieldId: "email" }] : []),
    ...(errors.password ? [{ text: errors.password, fieldId: "password" }] : []),
    ...(formError ? [{ text: authErrorText(formError) }] : []),
  ];
  const hint = formError?.code === ERROR_CODES.invalidCredentials ? copy.auth.errors.lockoutHint : undefined;

  return (
    <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-4" aria-busy={busy || undefined}>
      <ReasonNotice text={items.length === 0 ? notice : null} />
      <ErrorSummary items={items} hint={hint} focusKey={focusKey} />
      <FormField id="email" label={copy.login.email} error={errors.email}>
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
      <FormField id="password" label={copy.login.password} error={errors.password}>
        {(field) => (
          <PasswordInput
            {...field}
            name="password"
            autoComplete="current-password"
            placeholder={copy.login.passwordPlaceholder}
          />
        )}
      </FormField>
      <BusyButton type="submit" busy={busy} size="lg" className="mt-2 h-10 w-full">
        {copy.actions.signIn}
      </BusyButton>
      <Button
        type="button"
        variant="ghost"
        onClick={() => toast.info(copy.login.forgotHint)}
        className="h-10 self-center text-muted-foreground"
      >
        {copy.login.forgotPassword}
      </Button>
    </form>
  );
}
