"use client";

import { useState } from "react";
import { ErrorSummary } from "@/components/auth/error-summary";
import { PasswordInput } from "@/components/auth/password-input";
import { BusyButton } from "@/components/shared/busy-button";
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
import { copy } from "@/lib/copy";

const text = copy.settings.profile.confirm;
const IDS = { password: "confirm-identity-password", code: "confirm-identity-code" };

/** Turns the backend's answer into messages for the two fields or one general line. */
function problemFrom(error, conflictText) {
  if (error instanceof ApiError) {
    if (error.status === 400 && (error.errors?.password || error.errors?.code)) {
      const locked = /too many/i.test(error.errors.password?.[0] ?? "");
      return {
        fields: {
          ...(error.errors.password && { password: locked ? text.locked : text.passwordWrong }),
          ...(error.errors.code && { code: text.codeWrong }),
        },
      };
    }
    if (error.status === 429) return { general: text.rateLimited };
  }
  return { general: errorMessage(error, conflictText ? { 409: conflictText } : {}) };
}

function IdentityForm({ open, onOpenChange, title, description, submitLabel, busyLabel, destructive, conflictText, onConfirm, children }) {
  const returnFocus = useReturnFocus(open);
  const [password, setPassword] = useState("");
  const [code, setCode] = useState("");
  const [errors, setErrors] = useState({});
  const [general, setGeneral] = useState("");
  const [focusKey, setFocusKey] = useState(0);
  const [busy, setBusy] = useState(false);

  function fail(fields, message) {
    setErrors(fields);
    setGeneral(message);
    // The error summary takes focus and links to the fields.
    setFocusKey((key) => key + 1);
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const cleanCode = code.replace(/\s/g, "");
    const next = {};
    if (!password) next.password = text.passwordRequired;
    if (!cleanCode) next.code = text.codeRequired;
    else if (!/^\d{6}$/.test(cleanCode)) next.code = text.codeFormat;
    if (Object.keys(next).length > 0) return fail(next, "");

    setBusy(true);
    setErrors({});
    setGeneral("");

    try {
      await onConfirm({ password, code: cleanCode });
    } catch (error) {
      setBusy(false);
      const problem = problemFrom(error, conflictText);
      fail(problem.fields ?? {}, problem.general ?? "");
    }
  }

  const summary = [
    ...(errors.password ? [{ text: errors.password, fieldId: IDS.password }] : []),
    ...(errors.code ? [{ text: errors.code, fieldId: IDS.code }] : []),
    ...(general ? [{ text: general }] : []),
  ];

  return (
    <Dialog open={open} onOpenChange={(next) => !busy && onOpenChange(next)}>
      <DialogContent className="sm:max-w-md" onCloseAutoFocus={returnFocus}>
        <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-5" aria-busy={busy || undefined}>
          <DialogHeader>
            <DialogTitle className="text-lg font-semibold">{title}</DialogTitle>
            <DialogDescription>{description}</DialogDescription>
          </DialogHeader>

          {children}

          <ErrorSummary items={summary} focusKey={focusKey} />

          <FormField id={IDS.password} label={text.password} hint={text.passwordHint} error={errors.password}>
            {(field) => (
              <PasswordInput
                {...field}
                name="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            )}
          </FormField>

          <FormField id={IDS.code} label={text.code} hint={text.codeHint} error={errors.code}>
            {(field) => (
              <Input
                {...field}
                name="code"
                inputMode="numeric"
                autoComplete="one-time-code"
                spellCheck={false}
                value={code}
                onChange={(event) => setCode(event.target.value)}
                className="h-10 w-40 tracking-widest"
              />
            )}
          </FormField>

          <DialogFooter className="mx-0 mb-0 rounded-none border-0 bg-transparent p-0">
            <DialogClose asChild>
              <Button type="button" variant="outline" className="h-10 px-4" disabled={busy}>
                {copy.actions.cancel}
              </Button>
            </DialogClose>
            <BusyButton type="submit" busy={busy} variant={destructive ? "destructive" : "default"} className="h-10 px-4">
              {busy ? busyLabel : submitLabel}
            </BusyButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

/**
 * Asks for the password and a current authenticator code before a sensitive
 * action. `onConfirm({ password, code })` does the work and throws when it
 * fails; the backend's answer is shown on the fields. On success the caller
 * closes the dialog (or leaves the page). Each opening starts empty.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void, title: string,
 *   description: string, submitLabel: string, busyLabel: string,
 *   destructive?: boolean, conflictText?: string,
 *   onConfirm: (values: { password: string, code: string }) => Promise<void>,
 *   children?: React.ReactNode }} props
 */
export function ConfirmIdentityDialog(props) {
  const opening = useOpeningKey(props.open);
  if (!opening) return null;

  return <IdentityForm key={opening} {...props} />;
}
