"use client";

import { useEffect, useRef, useState } from "react";
import { CircleAlert, CircleCheck } from "lucide-react";
import { SelectField, controlClass } from "@/components/landing/form-controls";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { Input } from "@/components/ui/input";
import { requestAccess } from "@/lib/access-requests";
import { isEmail } from "@/lib/validation";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.landing.register;

// Field order, used to focus the first error.
const ORDER = ["name", "email", "organisation", "role"];
const NOTE_MAX = 500;

function read(form) {
  const data = new FormData(form);
  return Object.fromEntries([...ORDER, "note"].map((key) => [key, String(data.get(key) ?? "")]));
}

function validate(values) {
  const errors = {};
  if (!values.name.trim()) errors.name = text.errors.name;
  if (!values.email.trim()) errors.email = text.errors.emailRequired;
  else if (!isEmail(values.email.trim())) errors.email = text.errors.emailInvalid;
  if (!values.organisation.trim()) errors.organisation = text.errors.organisation;
  if (!values.role) errors.role = text.errors.role;
  if (values.note.length > NOTE_MAX) errors.note = text.errors.note;
  return errors;
}

/** The inline message for a field the API rejected. */
function fieldMessage(field) {
  switch (field) {
    case "name":
      return text.errors.name;
    case "email":
      return text.errors.emailInvalid;
    case "organisation":
      return text.errors.organisation;
    case "role":
      return text.errors.role;
    case "note":
      return text.errors.note;
    default:
      return null;
  }
}

/**
 * The account request form behind every Register control. It checks the
 * fields in the browser, sends the request to the API and shows the
 * outcome. An Admin reads the request and creates the account.
 * @param {{ doneAction?: React.ReactNode, idPrefix?: string }} props
 *   doneAction: shown with the thank-you text (the dialog passes Close).
 */
export function RegisterForm({ doneAction, idPrefix = "register" }) {
  const [errors, setErrors] = useState({});
  const [formError, setFormError] = useState(null);
  const [tried, setTried] = useState(false);
  const [busy, setBusy] = useState(false);
  const [done, setDone] = useState(false);
  const formRef = useRef(null);
  const successRef = useRef(null);
  const id = (key) => `${idPrefix}-${key}`;

  useEffect(() => {
    if (done) successRef.current?.focus();
  }, [done]);

  async function handleSubmit(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const values = read(form);
    const nextErrors = validate(values);

    setTried(true);
    setErrors(nextErrors);
    setFormError(null);
    const first = ORDER.find((key) => nextErrors[key]) ?? (nextErrors.note ? "note" : null);
    if (first) {
      form.elements.namedItem(first)?.focus();
      return;
    }

    setBusy(true);
    const result = await requestAccess({
      name: values.name.trim(),
      email: values.email.trim(),
      organisation: values.organisation.trim(),
      role: values.role,
      note: values.note.trim(),
    });
    setBusy(false);

    if (result.ok) {
      setDone(true);
      return;
    }

    const rejected = {};
    for (const field of Object.keys(result.fields ?? {})) {
      const message = fieldMessage(field);
      if (message) rejected[field] = message;
    }
    if (Object.keys(rejected).length > 0) {
      setErrors(rejected);
      form.elements.namedItem(ORDER.find((key) => rejected[key]) ?? "note")?.focus();
      return;
    }

    setFormError(result.rateLimited ? text.errors.rateLimited : result.network ? text.errors.network : text.errors.generic);
  }

  if (done) {
    return (
      <div ref={successRef} tabIndex={-1} role="status" className="lead-success flex flex-col items-start gap-4 outline-none">
        <CircleCheck className="size-8 text-foreground" aria-hidden="true" />
        <p className="text-lg font-medium">{text.success}</p>
        <p className="text-sm text-muted-foreground">{text.successNote}</p>
        {doneAction}
      </div>
    );
  }

  return (
    <form
      ref={formRef}
      onSubmit={handleSubmit}
      onChange={tried ? (event) => setErrors(validate(read(event.currentTarget))) : undefined}
      noValidate
      className="flex flex-col gap-5"
    >
      <div className="grid gap-4">
        <FormField id={id("name")} label={text.name} error={errors.name}>
          {(field) => <Input {...field} name="name" autoComplete="name" className="h-11 px-3" disabled={busy} />}
        </FormField>
        <FormField id={id("email")} label={text.email} error={errors.email}>
          {(field) => (
            <Input
              {...field}
              name="email"
              type="email"
              autoComplete="email"
              placeholder={text.emailPlaceholder}
              className="h-11 px-3"
              disabled={busy}
            />
          )}
        </FormField>
        <FormField id={id("organisation")} label={text.organisation} error={errors.organisation}>
          {(field) => (
            <Input {...field} name="organisation" autoComplete="organization" className="h-11 px-3" disabled={busy} />
          )}
        </FormField>
        <FormField id={id("role")} label={text.role} hint={text.roleHint} error={errors.role}>
          {(field) => (
            <SelectField
              field={{ ...field, disabled: busy }}
              name="role"
              placeholder={text.rolePlaceholder}
              options={text.roleOptions}
            />
          )}
        </FormField>
        <FormField id={id("note")} label={text.note} error={errors.note}>
          {(field) => (
            <textarea
              {...field}
              name="note"
              rows={3}
              disabled={busy}
              className={cn(controlClass, "resize-y py-2.5")}
            />
          )}
        </FormField>
      </div>
      {formError && (
        <p role="alert" className="flex items-start gap-2 text-sm text-risk-flagged-foreground">
          <CircleAlert className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          {formError}
        </p>
      )}
      <div className="flex flex-col items-start gap-2">
        <BusyButton type="submit" size="lg" className="h-11 px-6" busy={busy}>
          {busy ? text.sending : text.submit}
        </BusyButton>
        <p className="text-xs text-muted-foreground">{text.privacy}</p>
      </div>
    </form>
  );
}
