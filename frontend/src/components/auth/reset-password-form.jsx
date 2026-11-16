"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { ErrorSummary } from "@/components/auth/error-summary";
import { passwordRules } from "@/components/auth/change-password-form";
import { PasswordInput } from "@/components/auth/password-input";
import { PasswordRules } from "@/components/auth/password-rules";
import { FormFallback } from "@/components/auth/form-fallback";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import { authErrorText, authRequest } from "@/lib/auth/client";
import { ERROR_CODES } from "@/lib/auth/core";
import { copy } from "@/lib/copy";

const text = copy.login.reset;
const FIELD_IDS = { next: "new-password", confirm: "confirm-password" };

// The length and distinct characters rules; "different from the current
// password" does not apply here.
const rulesFor = (password) => passwordRules(password, "").slice(0, 2);

/** Reads token and email from the link's fragment, then removes them from the address bar. */
function readLink() {
  const params = new URLSearchParams(window.location.hash.replace(/^#/, ""));
  const token = params.get("token") ?? "";
  const email = params.get("email") ?? "";
  window.history.replaceState(null, "", window.location.pathname);
  return token && email ? { token, email } : null;
}

function validate({ next, confirm }) {
  const errors = {};
  if (!next) errors.next = text.errors.nextRequired;
  else if (rulesFor(next).some((rule) => !rule.met)) errors.next = text.errors.rulesNotMet;
  if (!confirm) errors.confirm = text.errors.confirmRequired;
  else if (next && confirm !== next) errors.confirm = text.errors.mismatch;
  return errors;
}

/**
 * Sets a new password from the emailed link. The link keeps its token in the
 * fragment (never sent to a server or in a Referer); it is read once here,
 * kept in memory and sent only to /api/auth/reset-password.
 */
export function ResetPasswordForm() {
  const router = useRouter();
  const link = useRef(null);
  const titleRef = useRef(null);
  const [state, setState] = useState("reading");
  const [email, setEmail] = useState("");
  const [values, setValues] = useState({ next: "", confirm: "" });
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState(null);
  const [focusKey, setFocusKey] = useState(0);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    link.current = readLink();
    setEmail(link.current?.email ?? "");
    setState(link.current ? "form" : "invalid");
  }, []);

  useEffect(() => {
    if (state === "invalid") titleRef.current?.focus();
  }, [state]);

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
    if (busy) return;

    const fieldErrors = validate(values);
    if (Object.keys(fieldErrors).length > 0) return show(fieldErrors, null);

    setBusy(true);
    const result = await authRequest("/api/auth/reset-password", {
      email: link.current.email,
      token: link.current.token,
      newPassword: values.next,
    });

    if (result.ok) {
      link.current = null;
      router.replace("/login?reason=passwordReset");
      return;
    }

    setBusy(false);
    if (result.error.code === ERROR_CODES.invalidLink) return setState("invalid");
    if (result.error.code === ERROR_CODES.validation && result.error.fields?.newPassword) {
      return show({ next: text.errors.policy }, null);
    }
    show({}, result.error);
  }

  if (state === "reading") return <FormFallback rows={2} />;

  if (state === "invalid") {
    return (
      <div className="flex flex-col gap-4">
        <div role="alert" className="rounded-xl border border-glass-border bg-background/60 p-4 text-sm">
          <h2 ref={titleRef} tabIndex={-1} className="font-medium outline-none">
            {text.invalidTitle}
          </h2>
          <p className="mt-1 text-muted-foreground">{text.invalid}</p>
        </div>
        <Button asChild size="lg" className="h-10 w-full">
          <Link href="/forgot-password">{text.requestNew}</Link>
        </Button>
        <Link href="/login" className="w-fit self-center rounded-sm text-sm text-muted-foreground underline underline-offset-4">
          {copy.auth.backToSignIn}
        </Link>
      </div>
    );
  }

  const items = [
    ...["next", "confirm"].filter((name) => errors[name]).map((name) => ({ text: errors[name], fieldId: FIELD_IDS[name] })),
    ...(formError ? [{ text: authErrorText(formError) }] : []),
  ];

  return (
    <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-4" aria-busy={busy || undefined}>
      <p className="text-sm text-muted-foreground">{text.intro(email)}</p>
      <ErrorSummary items={items} focusKey={focusKey} />
      <PasswordRules rules={rulesFor(values.next)} />
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
