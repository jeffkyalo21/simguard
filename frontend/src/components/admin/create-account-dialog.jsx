"use client";

import { useState } from "react";
import { RefreshCw } from "lucide-react";
import { FormDialog } from "@/components/shared/form-dialog";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { isEmail } from "@/lib/validation";
import { copy } from "@/lib/copy";

const text = copy.admin.requests.dialog;
const ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";

/** A random 16 character password without look-alike characters. */
function generatePassword() {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  return Array.from(bytes, (byte) => ALPHABET[byte % ALPHABET.length]).join("");
}

function Body({ request, busy, onSubmit, onOpenChange, open }) {
  const [values, setValues] = useState({
    name: request.name,
    email: request.email,
    password: generatePassword(),
  });
  const [errors, setErrors] = useState({});
  const set = (key) => (event) => setValues((current) => ({ ...current, [key]: event.target.value }));

  function submit(event) {
    event.preventDefault();
    const next = {};
    if (!values.name.trim()) next.name = text.errors.name;
    if (!isEmail(values.email.trim())) next.email = text.errors.email;
    if (values.password.length < 12) next.password = text.errors.password;
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    onSubmit({ name: values.name.trim(), email: values.email.trim(), role: request.role, temporaryPassword: values.password });
  }

  return (
    <FormDialog open={open} onOpenChange={onOpenChange} title={text.title} submitLabel={text.submit} busy={busy} onSubmit={submit}>
      <FormField id="create-account-name" label={text.name} error={errors.name}>
        {(field) => <Input {...field} value={values.name} onChange={set("name")} autoComplete="off" className="h-10 px-3" />}
      </FormField>
      <FormField id="create-account-email" label={text.email} error={errors.email}>
        {(field) => <Input {...field} type="email" value={values.email} onChange={set("email")} autoComplete="off" className="h-10 px-3" />}
      </FormField>
      <p className="text-sm">
        <span className="text-muted-foreground">{text.role}: </span>
        <span className="font-medium">{copy.roles[request.role] ?? request.role}</span>
      </p>
      <FormField id="create-account-password" label={text.password} hint={text.passwordHint} error={errors.password}>
        {(field) => (
          <div className="flex gap-2">
            <Input {...field} value={values.password} onChange={set("password")} autoComplete="off" spellCheck={false} className="h-10 px-3 font-mono" />
            <Button type="button" variant="outline" className="h-10 shrink-0 px-3" onClick={() => setValues((c) => ({ ...c, password: generatePassword() }))}>
              <RefreshCw aria-hidden="true" />
              {text.regenerate}
            </Button>
          </div>
        )}
      </FormField>
    </FormDialog>
  );
}

/**
 * Create-user dialog prefilled from an access request. The role is the one
 * requested. Remounts per request so the fields and the password start fresh.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void, request: object | null,
 *   busy: boolean, onSubmit: (body: object) => void }} props
 */
export function CreateAccountDialog({ request, ...props }) {
  if (!request) return null;
  return <Body key={request.id} request={request} {...props} />;
}
