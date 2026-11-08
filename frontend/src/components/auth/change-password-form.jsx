"use client";

import { useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { Check, Circle } from "lucide-react";
import { ErrorSummary } from "@/components/auth/error-summary";
import { PasswordInput } from "@/components/auth/password-input";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { authErrorText, authRequest, destinationAfterSignIn, loginHref } from "@/lib/auth/client";
import { ERROR_CODES } from "@/lib/auth/core";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.auth.changePassword;

// Same policy as the backend (backend/docs/auth.md): at least 12
// characters, at least 4 distinct characters, different from the current one.
export const PASSWORD_MIN_LENGTH = 12;
export const PASSWORD_MIN_DISTINCT = 4;

export function passwordRules(next, current) {
  return [
    { key: "length", met: next.length >= PASSWORD_MIN_LENGTH },
    { key: "distinct", met: new Set(next).size >= PASSWORD_MIN_DISTINCT },
    { key: "different", met: next.length > 0 && next !== current },
  ];
}

function validate({ current, next, confirm }) {
  const errors = {};
  if (!current) errors.current = text.errors.currentRequired;
  if (!next) errors.next = text.errors.nextRequired;
  else if (passwordRules(next, current).some((rule) => !rule.met)) errors.next = text.errors.rulesNotMet;
  if (!confirm) errors.confirm = text.errors.confirmRequired;
  else if (next && confirm !== next) errors.confirm = text.errors.mismatch;
  return errors;
}

/** Fields the backend may flag, mapped to this form's inputs and copy. */
function backendFieldErrors(fields = {}) {
  const errors = {};
  if (fields.currentPassword) errors.current = text.errors.currentWrong;
  if (fields.newPassword) errors.next = text.errors.policy;
  return errors;
}

const FIELD_IDS = { current: "current-password", next: "new-password", confirm: "confirm-password" };

/**
 * Forced password change. The rules are listed before submitting and tick
 * as they are met.
 */
export function ChangePasswordForm() {
  const router = useRouter();
  const nextPath = useSearchParams().get("next");
  const [values, setValues] = useState({ current: "", next: "", confirm: "" });
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState(null);
  const [focusKey, setFocusKey] = useState(0);
  const [busy, setBusy] = useState(false);

  const rules = passwordRules(values.next, values.current);

  function update(name) {
    return (event) => setValues((previous) => ({ ...previous, [name]: event.target.value }));
  }

  function show(fieldErrors, error) {
    setErrors(fieldErrors);
    setFormError(error);
    setFocusKey((key) => key + 1);
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const fieldErrors = validate(values);
    if (Object.keys(fieldErrors).length > 0) return show(fieldErrors, null);

    setBusy(true);
    const result = await authRequest("/api/auth/change-password", {
      currentPassword: values.current,
      newPassword: values.next,
    });

    if (!result.ok) {
      setBusy(false);
      if (result.error.code === ERROR_CODES.unauthorized) {
        router.replace(loginHref({ next: nextPath, reason: "expired" }));
        return;
      }
      if (result.error.code === ERROR_CODES.validation) {
        const mapped = backendFieldErrors(result.error.fields);
        if (Object.keys(mapped).length > 0) return show(mapped, null);
      }
      return show({}, result.error);
    }

    router.replace(destinationAfterSignIn({ role: result.data.role, next: nextPath }));
  }

  const items = [
    ...["current", "next", "confirm"]
      .filter((name) => errors[name])
      .map((name) => ({ text: errors[name], fieldId: FIELD_IDS[name] })),
    ...(formError ? [{ text: authErrorText(formError) }] : []),
  ];

  return (
    <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-4" aria-busy={busy || undefined}>
      <ErrorSummary items={items} focusKey={focusKey} />
      <FormField id={FIELD_IDS.current} label={text.current} error={errors.current}>
        {(field) => (
          <PasswordInput
            {...field}
            name="currentPassword"
            autoComplete="current-password"
            value={values.current}
            onChange={update("current")}
          />
        )}
      </FormField>

      <section aria-labelledby="password-rules-title" className="rounded-xl border border-glass-border bg-background/60 p-4">
        <h2 id="password-rules-title" className="text-sm font-medium">
          {text.rulesTitle}
        </h2>
        <ul id="password-rules" className="mt-2 flex flex-col gap-1.5 text-sm">
          {rules.map((rule) => (
            <li key={rule.key} className={cn("flex items-center gap-2", rule.met ? "text-risk-cleared-foreground" : "text-muted-foreground")}>
              {rule.met ? <Check className="size-4" aria-hidden="true" /> : <Circle className="size-4" aria-hidden="true" />}
              <span>
                {text.rules[rule.key]}
                <span className="sr-only">, {rule.met ? text.ruleMet : text.ruleNotMet}</span>
              </span>
            </li>
          ))}
        </ul>
      </section>

      <FormField id={FIELD_IDS.next} label={text.next} error={errors.next}>
        {(field) => (
          <PasswordInput
            {...field}
            aria-describedby={[field["aria-describedby"], "password-rules"].filter(Boolean).join(" ")}
            name="newPassword"
            autoComplete="new-password"
            value={values.next}
            onChange={update("next")}
          />
        )}
      </FormField>
      <FormField id={FIELD_IDS.confirm} label={text.confirm} error={errors.confirm}>
        {(field) => (
          <PasswordInput
            {...field}
            name="confirmPassword"
            autoComplete="new-password"
            value={values.confirm}
            onChange={update("confirm")}
          />
        )}
      </FormField>
      <BusyButton type="submit" busy={busy} size="lg" className="mt-2 h-10 w-full">
        {text.submit}
      </BusyButton>
    </form>
  );
}
