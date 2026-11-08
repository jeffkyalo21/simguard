"use client";

import { useState } from "react";
import { toast } from "sonner";
import { MoreHorizontal, UserPlus } from "lucide-react";
import { ChangeRoleDialog } from "@/components/admin/change-role-dialog";
import { InviteUserDialog } from "@/components/admin/invite-user-dialog";
import { useAuth } from "@/components/auth/auth-provider";
import { DataTable } from "@/components/data/data-table";
import { Pagination } from "@/components/data/pagination";
import { SectionCard } from "@/components/data/section-card";
import { ConfirmDialog } from "@/components/shared/confirm-dialog";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { Button } from "@/components/ui/button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu";
import { usePermission } from "@/hooks/use-permission";
import { useResource } from "@/hooks/use-resource";
import { ApiError, errorMessage } from "@/lib/api";
import { disableUser, enableUser, listUsers, resendSignInEmail, resetUserMfa, unlockUser } from "@/lib/data";
import { formatDateTime } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.admin.users;
const PAGE_SIZE = 20;

function statusOf(user) {
  if (user.isDisabled) return text.status.disabled;
  if (user.lockedOutUntil) return text.status.locked(formatDateTime(user.lockedOutUntil));
  if (user.mustChangePassword) return text.status.invited;
  return text.status.active;
}

// What each confirmed action calls and says. Role changes and invitations
// have their own dialogs; unlock and enable need no confirmation.
const confirmations = {
  disable: {
    title: text.confirm.disableTitle,
    description: (user) => text.confirm.disableText(user.name, user.email),
    label: text.actions.disable,
    run: (user) => disableUser(user.id),
    done: (user) => text.toasts.disabled(user.name),
  },
  resetMfa: {
    title: text.confirm.resetMfaTitle,
    description: (user) => text.confirm.resetMfaText(user.name),
    label: text.confirm.resetMfa,
    run: (user) => resetUserMfa(user.id),
    done: (user) => text.toasts.mfaReset(user.name),
  },
  resend: {
    title: text.confirm.resendTitle,
    description: (user) => text.confirm.resendText(user.name),
    label: text.confirm.resend,
    run: (user) => resendSignInEmail(user.id),
    done: (user, result) => (result?.emailSent === false ? text.toasts.resentNoEmail(user.name) : text.toasts.resent(user.name)),
    warnWhen: (result) => result?.emailSent === false,
  },
};

// Your own row cannot change your role, disable you or replace your password
// (the API answers 409). The tooltip says why; inside a menu the wrapper is
// not focusable, so the item also carries the reason as screen reader text.
function SelfBlocked({ self, children }) {
  return (
    <DisabledReason reason={text.ownAccount} disabled={self} focusable={false}>
      {children}
    </DisabledReason>
  );
}

export function UsersPanel() {
  const [page, setPage] = useState(1);
  const resource = useResource(() => listUsers({ page, pageSize: PAGE_SIZE }), [page]);
  const managing = usePermission("users.manage");
  const { email: myEmail } = useAuth();
  const [inviting, setInviting] = useState(false);
  const [changingRole, setChangingRole] = useState({ open: false, user: null });
  const [confirming, setConfirming] = useState({ open: false, kind: "disable", user: null });
  const [busy, setBusy] = useState(false);

  const confirmation = confirmations[confirming.kind];

  async function runQuick(call, message, user) {
    try {
      await call(user.id);
      toast.success(message(user.name));
    } catch (error) {
      toast.error(errorMessage(error));
    }
  }

  async function confirm() {
    const { user } = confirming;
    setBusy(true);
    try {
      const result = await confirmation.run(user);
      const message = confirmation.done(user, result);
      if (confirmation.warnWhen?.(result)) toast.warning(message);
      else toast.success(message);
    } catch (error) {
      const messages = {};
      if (error instanceof ApiError && error.status === 409) {
        messages[409] = confirming.kind === "resend" ? text.toasts.disabledAccount : text.toasts.ownOrLastAdmin;
      }
      toast.error(errorMessage(error, messages));
    } finally {
      setConfirming((current) => ({ ...current, open: false }));
      setBusy(false);
    }
  }

  const columns = [
    {
      key: "person",
      header: text.columns.person,
      cell: (user) => (
        <span className="flex flex-col">
          <span className="font-medium">
            {user.name}
            {user.email === myEmail && <span className="font-normal text-muted-foreground">{` (${text.you})`}</span>}
          </span>
          <span className="text-sm text-muted-foreground">{user.email}</span>
        </span>
      ),
    },
    { key: "role", header: text.columns.role, cell: (user) => copy.roles[user.role] ?? user.role },
    {
      key: "status",
      header: text.columns.status,
      cell: (user) => (
        <span className="flex flex-col">
          <span>{statusOf(user)}</span>
          <span className="text-sm text-muted-foreground">{user.mfaEnabled ? text.mfa.on : text.mfa.off}</span>
        </span>
      ),
    },
    {
      key: "actions",
      header: text.columns.actions,
      cell: (user) => {
        const self = user.email === myEmail;
        return (
          <DisabledReason reason={managing.reason ?? ""} disabled={!managing.allowed}>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" className="h-9 px-3" disabled={!managing.allowed} aria-label={text.actionsFor(user.name)}>
                  <MoreHorizontal aria-hidden="true" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <SelfBlocked self={self}>
                  <DropdownMenuItem disabled={self} className="w-full" onSelect={() => setChangingRole({ open: true, user })}>
                    {text.actions.changeRole}
                    {self && <span className="sr-only">{text.ownAccount}</span>}
                  </DropdownMenuItem>
                </SelfBlocked>
                <SelfBlocked self={self}>
                  <DropdownMenuItem
                    disabled={self || user.isDisabled}
                    className="w-full"
                    onSelect={() => setConfirming({ open: true, kind: "resend", user })}
                  >
                    {text.actions.resend}
                    {self && <span className="sr-only">{text.ownAccount}</span>}
                  </DropdownMenuItem>
                </SelfBlocked>
                {user.lockedOutUntil && (
                  <DropdownMenuItem onSelect={() => runQuick(unlockUser, text.toasts.unlocked, user)}>{text.actions.unlock}</DropdownMenuItem>
                )}
                <DropdownMenuItem onSelect={() => setConfirming({ open: true, kind: "resetMfa", user })}>
                  {text.actions.resetMfa}
                </DropdownMenuItem>
                {user.isDisabled ? (
                  <DropdownMenuItem onSelect={() => runQuick(enableUser, text.toasts.enabled, user)}>{text.actions.enable}</DropdownMenuItem>
                ) : (
                  <SelfBlocked self={self}>
                    <DropdownMenuItem disabled={self} className="w-full" onSelect={() => setConfirming({ open: true, kind: "disable", user })}>
                      {text.actions.disable}
                      {self && <span className="sr-only">{text.ownAccount}</span>}
                    </DropdownMenuItem>
                  </SelfBlocked>
                )}
              </DropdownMenuContent>
            </DropdownMenu>
          </DisabledReason>
        );
      },
    },
  ];

  return (
    <>
      <p className="max-w-2xl text-sm text-muted-foreground">{text.intro}</p>
      <SectionCard
        title={text.title}
        variant="strong"
        flush
        className="overflow-hidden"
        action={
          <DisabledReason reason={managing.reason ?? ""} disabled={!managing.allowed}>
            <Button className="h-10 px-4" disabled={!managing.allowed} onClick={() => setInviting(true)}>
              <UserPlus aria-hidden="true" />
              {text.invite}
            </Button>
          </DisabledReason>
        }
      >
        <DataTable
          caption={text.title}
          columns={columns}
          rows={resource.data?.items ?? null}
          getRowKey={(user) => user.id}
          error={resource.error}
          onRetry={resource.retry}
          emptyMessage={text.empty}
        />
        {resource.data && resource.data.total > PAGE_SIZE && (
          <div className="px-4 py-3">
            <Pagination page={page} pageSize={PAGE_SIZE} total={resource.data.total} onPageChange={setPage} />
          </div>
        )}
      </SectionCard>
      <InviteUserDialog open={inviting} onOpenChange={setInviting} />
      <ChangeRoleDialog
        open={changingRole.open}
        user={changingRole.user}
        onOpenChange={(open) => setChangingRole((current) => ({ ...current, open }))}
      />
      <ConfirmDialog
        open={confirming.open}
        onOpenChange={(open) => setConfirming((current) => ({ ...current, open }))}
        title={confirmation.title}
        description={confirming.user ? confirmation.description(confirming.user) : ""}
        confirmLabel={confirmation.label}
        busy={busy}
        onConfirm={confirm}
      />
    </>
  );
}
