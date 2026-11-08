"use client";

import { useState } from "react";
import { toast } from "sonner";
import { FormDialog } from "@/components/shared/form-dialog";
import { FormField } from "@/components/shared/form-field";
import { SelectField } from "@/components/sims/sim-form-dialog";
import { Input } from "@/components/ui/input";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { ApiError, errorMessage } from "@/lib/api";
import { inviteUser } from "@/lib/data";
import { isEmail } from "@/lib/validation";
import { copy } from "@/lib/copy";

const text = copy.admin.users.invitation;
const FIELD_ORDER = ["name", "email", "role"];

export const ROLE_OPTIONS = Object.entries(copy.roles).map(([value, label]) => ({ value, label }));

function focusFirstError(errors) {
  const first = FIELD_ORDER.find((field) => errors[field]);
  if (first) document.getElementById(`invite-${first}`)?.focus();
}

/** The fields. Keyed by the caller, so each opening starts fresh. */
function InviteForm({ open, onOpenChange }) {
  const [values, setValues] = useState({ name: "", email: "", role: "" });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const set = (key) => (event) => setValues((current) => ({ ...current, [key]: event.target.value }));

  async function submit(event) {
    event.preventDefault();
    const next = {};
    if (!values.name.trim()) next.name = text.errors.name;
    if (!isEmail(values.email.trim())) next.email = text.errors.email;
    if (!values.role) next.role = text.errors.role;
    setErrors(next);
    if (Object.keys(next).length > 0) return focusFirstError(next);

    setBusy(true);
    try {
      const name = values.name.trim();
      const invited = await inviteUser({ name, email: values.email.trim(), role: values.role });
      if (invited?.emailSent === false) toast.warning(copy.admin.users.toasts.invitedNoEmail(name));
      else toast.success(copy.admin.users.toasts.invited(name));
      onOpenChange(false);
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        const taken = { email: text.errors.emailTaken };
        setErrors(taken);
        focusFirstError(taken);
      } else if (error instanceof ApiError && error.status === 400 && error.errors.email) {
        const invalid = { email: text.errors.email };
        setErrors(invalid);
        focusFirstError(invalid);
      } else {
        toast.error(errorMessage(error));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog open={open} onOpenChange={onOpenChange} title={text.title} submitLabel={text.submit} busy={busy} onSubmit={submit}>
      <FormField id="invite-name" label={text.name} error={errors.name}>
        {(field) => <Input {...field} value={values.name} onChange={set("name")} autoComplete="off" className="h-10 px-3" />}
      </FormField>
      <FormField id="invite-email" label={text.email} error={errors.email}>
        {(field) => <Input {...field} type="email" value={values.email} onChange={set("email")} autoComplete="off" className="h-10 px-3" />}
      </FormField>
      <SelectField
        id="invite-role"
        label={text.role}
        value={values.role}
        options={ROLE_OPTIONS}
        error={errors.role}
        placeholder={text.rolePlaceholder}
        onChange={(role) => setValues((current) => ({ ...current, role }))}
      />
      <p className="text-sm text-muted-foreground">{text.hint}</p>
    </FormDialog>
  );
}

/**
 * Invite a person: name, email and role. SimGuard generates the password and
 * emails it, so nobody types or sees it here.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void }} props
 */
export function InviteUserDialog(props) {
  const opening = useOpeningKey(props.open);
  return <InviteForm key={opening} {...props} />;
}
