"use client";

import { useState } from "react";
import { toast } from "sonner";
import { Check, Circle, Copy, ShieldCheck } from "lucide-react";
import { useAuth } from "@/components/auth/auth-provider";
import { PasswordInput } from "@/components/auth/password-input";
import { GlassCard } from "@/components/glass/glass-card";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { ConfirmIdentityDialog } from "@/components/settings/confirm-identity-dialog";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { useReturnFocus } from "@/hooks/use-return-focus";
import { ApiError, errorMessage } from "@/lib/api";
import { authRequest, landingHref, loginHref, signOutRequest } from "@/lib/auth/client";
import { ERROR_CODES, ROLES } from "@/lib/auth/core";
import { deleteMyAccount, regenerateRecoveryCodes, updateProfileName } from "@/lib/data";
import { copy } from "@/lib/copy";
import { passwordRules } from "@/components/auth/change-password-form";
import { cn } from "@/lib/utils";

const text = copy.settings.profile;
const NAME_MAX = 100;

function Section({ id, title, children, danger = false }) {
  return (
    <GlassCard
      as="section"
      variant="strong"
      aria-labelledby={id}
      className={cn("flex flex-col gap-4 p-5", danger && "border-risk-flagged-foreground/40")}
    >
      <h2 id={id} className="text-base font-semibold">
        {title}
      </h2>
      {children}
    </GlassCard>
  );
}

function AccountSection() {
  const { name, email, role, refresh } = useAuth();
  const [value, setValue] = useState(name ?? "");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const t = text.account;

  async function handleSubmit(event) {
    event.preventDefault();
    const trimmed = value.trim();
    if (!trimmed) return fail(t.nameRequired);
    if (trimmed.length > NAME_MAX) return fail(t.nameTooLong);

    setBusy(true);
    setError("");
    try {
      await updateProfileName(trimmed);
      setValue(trimmed);
      await refresh();
      toast.success(t.saved);
    } catch (problem) {
      if (problem instanceof ApiError && problem.errors?.name?.[0]) fail(problem.errors.name[0]);
      else toast.error(errorMessage(problem));
    } finally {
      setBusy(false);
    }
  }

  function fail(message) {
    setError(message);
    document.getElementById("profile-name")?.focus();
  }

  return (
    <Section id="profile-account" title={t.title}>
      <form onSubmit={handleSubmit} noValidate className="flex max-w-md flex-col gap-4">
        <FormField id="profile-name" label={t.name} error={error}>
          {(field) => (
            <Input
              {...field}
              name="name"
              autoComplete="name"
              value={value}
              onChange={(event) => setValue(event.target.value)}
              className="h-10"
            />
          )}
        </FormField>
        <FormField id="profile-email" label={t.email} hint={role === ROLES.admin ? t.readOnlyAdmin : t.readOnly}>
          {(field) => <Input {...field} value={email ?? ""} readOnly className="h-10" />}
        </FormField>
        <FormField id="profile-role" label={t.role}>
          {(field) => <Input {...field} value={copy.roles[role] ?? role ?? ""} readOnly className="h-10" />}
        </FormField>
        <div>
          <BusyButton type="submit" busy={busy} className="h-10 px-4">
            {busy ? t.saving : t.save}
          </BusyButton>
        </div>
      </form>
    </Section>
  );
}

const PASSWORD_IDS = { current: "profile-current-password", next: "profile-new-password", confirm: "profile-confirm-password" };

function PasswordSection() {
  const labels = copy.auth.changePassword;
  const t = text.password;
  const [values, setValues] = useState({ current: "", next: "", confirm: "" });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const rules = passwordRules(values.next, values.current);

  function update(name) {
    return (event) => setValues((previous) => ({ ...previous, [name]: event.target.value }));
  }

  function fail(next) {
    setErrors(next);
    const first = ["current", "next", "confirm"].find((name) => next[name]);
    if (first) document.getElementById(PASSWORD_IDS[first])?.focus();
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const next = {};
    if (!values.current) next.current = labels.errors.currentRequired;
    if (!values.next) next.next = labels.errors.nextRequired;
    else if (rules.some((rule) => !rule.met)) next.next = labels.errors.rulesNotMet;
    if (!values.confirm) next.confirm = labels.errors.confirmRequired;
    else if (values.next && values.confirm !== values.next) next.confirm = labels.errors.mismatch;
    if (Object.keys(next).length > 0) return fail(next);

    setBusy(true);
    setErrors({});
    const result = await authRequest("/api/auth/change-password", {
      currentPassword: values.current,
      newPassword: values.next,
    });
    setBusy(false);

    if (result.ok) {
      setValues({ current: "", next: "", confirm: "" });
      toast.success(t.changed);
      return;
    }
    if (result.error.code === ERROR_CODES.unauthorized) {
      window.location.assign(loginHref({ reason: "expired" }));
      return;
    }
    if (result.error.code === ERROR_CODES.validation) {
      const fields = result.error.fields ?? {};
      const mapped = {};
      if (fields.currentPassword) mapped.current = labels.errors.currentWrong;
      if (fields.newPassword) mapped.next = labels.errors.policy;
      if (Object.keys(mapped).length > 0) return fail(mapped);
    }
    toast.error(copy.errors.generic);
  }

  return (
    <Section id="profile-password" title={t.title}>
      <p className="text-sm text-muted-foreground">{t.intro}</p>
      <form onSubmit={handleSubmit} noValidate className="flex max-w-md flex-col gap-4" aria-busy={busy || undefined}>
        <FormField id={PASSWORD_IDS.current} label={labels.current} error={errors.current}>
          {(field) => (
            <PasswordInput {...field} name="currentPassword" autoComplete="current-password" value={values.current} onChange={update("current")} />
          )}
        </FormField>
        <ul id="profile-password-rules" aria-label={labels.rulesTitle} className="flex flex-col gap-1.5 text-sm">
          {rules.map((rule) => (
            <li key={rule.key} className={cn("flex items-center gap-2", rule.met ? "text-foreground" : "text-muted-foreground")}>
              {rule.met ? <Check className="size-4" aria-hidden="true" /> : <Circle className="size-4" aria-hidden="true" />}
              <span>
                {labels.rules[rule.key]}
                <span className="sr-only">, {rule.met ? labels.ruleMet : labels.ruleNotMet}</span>
              </span>
            </li>
          ))}
        </ul>
        <FormField id={PASSWORD_IDS.next} label={labels.next} error={errors.next}>
          {(field) => (
            <PasswordInput
              {...field}
              aria-describedby={[field["aria-describedby"], "profile-password-rules"].filter(Boolean).join(" ")}
              name="newPassword"
              autoComplete="new-password"
              value={values.next}
              onChange={update("next")}
            />
          )}
        </FormField>
        <FormField id={PASSWORD_IDS.confirm} label={labels.confirm} error={errors.confirm}>
          {(field) => (
            <PasswordInput {...field} name="confirmPassword" autoComplete="new-password" value={values.confirm} onChange={update("confirm")} />
          )}
        </FormField>
        <div>
          <BusyButton type="submit" busy={busy} className="h-10 px-4">
            {busy ? t.submitting : t.submit}
          </BusyButton>
        </div>
      </form>
    </Section>
  );
}

/** The new codes, shown once. Cannot be dismissed by a click outside, so they are not lost by accident. */
function RecoveryCodesDialog({ codes, onDone }) {
  const t = text.twoFactor;
  const returnFocus = useReturnFocus(Boolean(codes));

  async function copyAll() {
    try {
      await navigator.clipboard.writeText(codes.join("\n"));
      toast.success(t.copied);
    } catch {
      toast.error(t.copyFailed);
    }
  }

  return (
    <Dialog open={Boolean(codes)} onOpenChange={(next) => !next && onDone()}>
      <DialogContent
        className="sm:max-w-md"
        showCloseButton={false}
        onCloseAutoFocus={returnFocus}
        onInteractOutside={(event) => event.preventDefault()}
        onEscapeKeyDown={(event) => event.preventDefault()}
      >
        <DialogHeader>
          <DialogTitle className="text-lg font-semibold">{t.resultTitle}</DialogTitle>
          <DialogDescription>{t.resultText}</DialogDescription>
        </DialogHeader>
        <ul aria-label={t.resultList} className="grid grid-cols-2 gap-2 rounded-xl border border-input p-3 font-mono text-sm">
          {(codes ?? []).map((code) => (
            <li key={code}>{code}</li>
          ))}
        </ul>
        <DialogFooter className="mx-0 mb-0 rounded-none border-0 bg-transparent p-0">
          <Button type="button" variant="outline" className="h-10 px-4" onClick={copyAll}>
            <Copy aria-hidden="true" />
            {t.copyAll}
          </Button>
          <Button type="button" className="h-10 px-4" onClick={onDone}>
            {t.done}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function TwoFactorSection() {
  const t = text.twoFactor;
  const [asking, setAsking] = useState(false);
  const [codes, setCodes] = useState(null);

  return (
    <Section id="profile-two-factor" title={t.title}>
      <p className="flex items-center gap-2 text-sm font-medium">
        <ShieldCheck className="size-4" aria-hidden="true" />
        {t.status}
      </p>
      <p className="text-sm text-muted-foreground">{t.statusHint}</p>
      <div>
        <Button type="button" variant="outline" className="h-10 px-4" onClick={() => setAsking(true)}>
          {t.newCodes}
        </Button>
      </div>

      <ConfirmIdentityDialog
        open={asking}
        onOpenChange={setAsking}
        title={t.dialogTitle}
        description={t.dialogText}
        submitLabel={t.submit}
        busyLabel={t.submitting}
        onConfirm={async (values) => {
          const result = await regenerateRecoveryCodes(values);
          setAsking(false);
          setCodes(result.recoveryCodes);
        }}
      />
      <RecoveryCodesDialog codes={codes} onDone={() => setCodes(null)} />
    </Section>
  );
}

function DangerSection() {
  const { role } = useAuth();
  const t = text.danger;
  const [asking, setAsking] = useState(false);
  const isAdmin = role === ROLES.admin;

  return (
    <Section id="profile-danger" title={t.title} danger>
      <p className="text-sm text-muted-foreground">{t.intro}</p>
      {isAdmin && (
        <p id="profile-danger-reason" className="text-sm">
          {t.adminBlocked}
        </p>
      )}
      <div>
        <Button
          type="button"
          variant="destructive"
          className="h-10 px-4"
          disabled={isAdmin}
          aria-describedby={isAdmin ? "profile-danger-reason" : undefined}
          onClick={() => setAsking(true)}
        >
          {t.button}
        </Button>
      </div>

      <ConfirmIdentityDialog
        open={asking}
        onOpenChange={setAsking}
        title={t.dialogTitle}
        description={t.dialogLead}
        submitLabel={t.confirmLabel}
        busyLabel={t.submitting}
        destructive
        conflictText={t.lastAdmin}
        onConfirm={async (values) => {
          await deleteMyAccount(values);
          // The session ended on the server; drop the cookies and leave for the landing page.
          await signOutRequest();
          // The dialog stays busy while the page changes.
          window.location.assign(landingHref());
        }}
      >
        <ul className="flex list-disc flex-col gap-1.5 pl-5 text-sm">
          {t.effects.map((effect) => (
            <li key={effect}>{effect}</li>
          ))}
        </ul>
      </ConfirmIdentityDialog>
    </Section>
  );
}

/** Settings, Profile: details, password, two-factor and the danger area. Every role. */
export function ProfilePanel() {
  return (
    <div className="flex flex-col gap-4">
      <AccountSection />
      <PasswordSection />
      <TwoFactorSection />
      <DangerSection />
    </div>
  );
}
