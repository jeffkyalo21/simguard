"use client";

import { useState } from "react";
import { toast } from "sonner";
import { DataTable } from "@/components/data/data-table";
import { SectionCard } from "@/components/data/section-card";
import { CreateAccountDialog } from "@/components/admin/create-account-dialog";
import { ConfirmDialog } from "@/components/shared/confirm-dialog";
import { DisabledReason } from "@/components/shared/disabled-reason";
import { Button } from "@/components/ui/button";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { usePermission } from "@/hooks/use-permission";
import { useResource } from "@/hooks/use-resource";
import { ApiError, errorMessage } from "@/lib/api";
import { createUser, listAccessRequests, reviewAccessRequest } from "@/lib/data";
import { formatDateTime } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.admin.requests;
const STATUSES = ["pending", "approved", "declined"];

export function RequestsPanel() {
  const [status, setStatus] = useState("pending");
  const resource = useResource(() => listAccessRequests({ status, pageSize: 100 }), [status]);
  const managing = usePermission("users.manage");
  const [creating, setCreating] = useState({ open: false, request: null });
  const [declining, setDeclining] = useState({ open: false, request: null });
  const [busy, setBusy] = useState(false);

  async function create(body) {
    const { request } = creating;
    setBusy(true);
    try {
      await createUser(body);
    } catch (error) {
      toast.error(errorMessage(error));
      setBusy(false);
      return;
    }
    try {
      await reviewAccessRequest(request.id, "approved");
      toast.success(text.toasts.created(body.name));
    } catch {
      toast.warning(text.toasts.createdNotMarked(body.name));
    }
    setCreating((current) => ({ ...current, open: false }));
    setBusy(false);
  }

  async function decline() {
    const { request } = declining;
    setBusy(true);
    try {
      await reviewAccessRequest(request.id, "declined");
      toast.success(text.toasts.declined(request.name));
    } catch (error) {
      toast.error(error instanceof ApiError && error.status === 409 ? text.toasts.alreadyReviewed : errorMessage(error));
    } finally {
      setDeclining((current) => ({ ...current, open: false }));
      setBusy(false);
    }
  }

  const columns = [
    {
      key: "person",
      header: text.columns.person,
      cell: (row) => (
        <span className="flex flex-col">
          <span className="font-medium">{row.name}</span>
          <span className="text-sm text-muted-foreground">{row.email}</span>
        </span>
      ),
    },
    { key: "organisation", header: text.columns.organisation, cell: (row) => row.organisation },
    { key: "role", header: text.columns.role, cell: (row) => copy.roles[row.role] ?? row.role },
    { key: "note", header: text.columns.note, cell: (row) => row.note || copy.common.noValue },
    { key: "requested", header: text.columns.requested, cell: (row) => formatDateTime(row.createdAt) },
    status === "pending"
      ? {
          key: "actions",
          header: text.columns.actions,
          cell: (row) => (
            <DisabledReason reason={managing.reason ?? ""} disabled={!managing.allowed}>
              <span className="flex gap-2">
                <Button
                  className="h-9 px-3"
                  disabled={!managing.allowed}
                  aria-label={text.createFor(row.name)}
                  onClick={() => setCreating({ open: true, request: row })}
                >
                  {text.createAccount}
                </Button>
                <Button
                  variant="outline"
                  className="h-9 px-3"
                  disabled={!managing.allowed}
                  aria-label={text.declineFor(row.name)}
                  onClick={() => setDeclining({ open: true, request: row })}
                >
                  {text.decline}
                </Button>
              </span>
            </DisabledReason>
          ),
        }
      : {
          key: "reviewed",
          header: text.columns.reviewed,
          cell: (row) => (row.reviewedAt ? text.reviewedBy(formatDateTime(row.reviewedAt), row.reviewedByEmail ?? copy.common.noValue) : copy.common.noValue),
        },
  ];

  return (
    <>
      <p className="max-w-2xl text-sm text-muted-foreground">{text.intro}</p>
      <SectionCard title={text.title} variant="strong" flush className="overflow-hidden">
        <div className="px-4 pt-2 pb-4">
          <Tabs value={status} onValueChange={setStatus}>
            <TabsList variant="pill" aria-label={text.statusFilter}>
              {STATUSES.map((value) => (
                <TabsTrigger key={value} value={value}>
                  {text.status[value]}
                </TabsTrigger>
              ))}
            </TabsList>
          </Tabs>
        </div>
        <DataTable
          caption={`${text.title}: ${text.status[status]}`}
          columns={columns}
          rows={resource.data?.items ?? null}
          getRowKey={(row) => row.id}
          error={resource.error}
          onRetry={resource.retry}
          emptyMessage={text.empty[status]}
        />
      </SectionCard>
      <CreateAccountDialog
        open={creating.open}
        onOpenChange={(open) => setCreating((current) => ({ ...current, open }))}
        request={creating.request}
        busy={busy}
        onSubmit={create}
      />
      <ConfirmDialog
        open={declining.open}
        onOpenChange={(open) => setDeclining((current) => ({ ...current, open }))}
        title={text.declineTitle}
        description={declining.request ? text.declineText(declining.request.name, declining.request.email) : ""}
        confirmLabel={text.decline}
        busy={busy}
        onConfirm={decline}
      />
    </>
  );
}
