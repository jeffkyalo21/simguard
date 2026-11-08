"use client";

import { useState } from "react";
import { toast } from "sonner";
import { ROLE_OPTIONS } from "@/components/admin/invite-user-dialog";
import { FormDialog } from "@/components/shared/form-dialog";
import { SelectField } from "@/components/sims/sim-form-dialog";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { ApiError, errorMessage } from "@/lib/api";
import { changeUserRole } from "@/lib/data";
import { copy } from "@/lib/copy";

const text = copy.admin.users.roleDialog;

/** The fields. Keyed by the caller, so each opening starts fresh. */
function RoleForm({ user, open, onOpenChange }) {
  const [role, setRole] = useState(user.role);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function submit(event) {
    event.preventDefault();
    if (!role) {
      setError(text.errors.role);
      return document.getElementById("change-role-role")?.focus();
    }
    if (role === user.role) return onOpenChange(false);

    setBusy(true);
    try {
      await changeUserRole(user.id, role);
      toast.success(copy.admin.users.toasts.roleChanged(user.name, copy.roles[role] ?? role));
      onOpenChange(false);
    } catch (failure) {
      toast.error(
        errorMessage(failure, failure instanceof ApiError && failure.status === 409 ? { 409: copy.admin.users.toasts.ownOrLastAdmin } : {})
      );
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog open={open} onOpenChange={onOpenChange} title={text.title(user.name)} submitLabel={text.submit} busy={busy} onSubmit={submit}>
      <SelectField
        id="change-role-role"
        label={text.role}
        value={role}
        options={ROLE_OPTIONS}
        error={error}
        onChange={(next) => {
          setRole(next);
          setError("");
        }}
      />
    </FormDialog>
  );
}

/**
 * Change one user's role. The API signs the user out of old tokens and
 * refuses your own account and the last active Admin (409).
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void, user: object | null }} props
 */
export function ChangeRoleDialog({ user, open, onOpenChange }) {
  const opening = useOpeningKey(open);
  if (!user) return null;
  return <RoleForm key={`${user.id}-${opening}`} user={user} open={open} onOpenChange={onOpenChange} />;
}
