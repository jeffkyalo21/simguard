"use client";

import { useState } from "react";
import { toast } from "sonner";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { DataTable } from "@/components/data/data-table";
import { GlassCard } from "@/components/glass/glass-card";
import { CarrierFormDialog } from "@/components/settings/carrier-form-dialog";
import { ConfirmDialog } from "@/components/shared/confirm-dialog";
import { DisabledReason } from "@/components/shared/disabled-reason";
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
import { usePermission } from "@/hooks/use-permission";
import { useResource } from "@/hooks/use-resource";
import { useReturnFocus } from "@/hooks/use-return-focus";
import { ApiError, errorMessage } from "@/lib/api";
import { listCarriers, removeCarrier } from "@/lib/data";
import { formatDate } from "@/lib/format";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.settings.carriers;

/** Says why a carrier was kept and how many SIMs hold it. */
function BlockedDialog({ open, onOpenChange, carrier }) {
  const returnFocus = useReturnFocus(open);
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-sm" showCloseButton={false} onCloseAutoFocus={returnFocus}>
        <DialogHeader>
          <DialogTitle className="text-lg font-semibold">{text.blocked.title}</DialogTitle>
          <DialogDescription>{carrier ? text.blocked.text(carrier.name, carrier.simCount) : ""}</DialogDescription>
        </DialogHeader>
        <DialogFooter className="mx-0 mb-0 rounded-none border-0 bg-transparent p-0">
          <DialogClose asChild>
            <Button type="button" className="h-10 px-4">
              {copy.actions.close}
            </Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

export function CarriersPanel() {
  const resource = useResource(() => listCarriers(), []);
  const carriers = resource.data;

  // Dialogs keep the carrier they opened with while they close.
  const [form, setForm] = useState({ open: false, carrier: null });
  const [removing, setRemoving] = useState({ open: false, carrier: null });
  const [blocked, setBlocked] = useState({ open: false, carrier: null });
  const [busy, setBusy] = useState(false);
  const editing = usePermission("carriers.edit");

  function askRemove(carrier) {
    if (carrier.simCount > 0) setBlocked({ open: true, carrier });
    else setRemoving({ open: true, carrier });
  }

  async function confirmRemove() {
    const { carrier } = removing;
    setBusy(true);
    try {
      await removeCarrier(carrier.id);
      setRemoving((current) => ({ ...current, open: false }));
      toast.success(text.toasts.removed(carrier.name));
    } catch (error) {
      setRemoving((current) => ({ ...current, open: false }));
      // 409: SIMs were moved onto the carrier after the list loaded.
      const simCount = error instanceof ApiError && error.status === 409 ? Number(error.extensions.simCount) : NaN;
      if (Number.isFinite(simCount)) setBlocked({ open: true, carrier: { ...carrier, simCount } });
      else toast.error(errorMessage(error));
    } finally {
      setBusy(false);
    }
  }

  const columns = [
    { key: "name", header: text.form.name, cell: (carrier) => <span className="font-medium">{carrier.name}</span> },
    { key: "sims", header: text.sims, cell: (carrier) => <span className="tabular-nums">{carrier.simCount.toLocaleString("en-KE")}</span> },
    { key: "created", header: text.created, cell: (carrier) => formatDate(carrier.createdAt) },
    {
      key: "actions",
      header: copy.columns.actions,
      srOnlyHeader: true,
      className: "w-56 pr-3 text-right",
      cell: (carrier) => (
        <div className="flex justify-end gap-2">
          <DisabledReason reason={editing.reason ?? ""} disabled={!editing.allowed}>
            <Button type="button" variant="outline" size="sm" className="h-9 px-3" disabled={!editing.allowed} onClick={() => setForm({ open: true, carrier })}>
              <Pencil aria-hidden="true" />
              {text.rename}
              <span className="sr-only">{text.rowContext(carrier.name)}</span>
            </Button>
          </DisabledReason>
          <DisabledReason reason={editing.reason ?? ""} disabled={!editing.allowed}>
            <Button type="button" variant="outline" size="sm" className="h-9 px-3" disabled={!editing.allowed} onClick={() => askRemove(carrier)}>
              <Trash2 aria-hidden="true" />
              {text.remove}
              <span className="sr-only">{text.rowContext(carrier.name)}</span>
            </Button>
          </DisabledReason>
        </div>
      ),
    },
  ];

  const target = removing.carrier;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground tabular-nums" aria-live="polite">
          {carriers ? text.count(carriers.length) : " "}
        </p>
        <DisabledReason reason={editing.reason ?? ""} disabled={!editing.allowed}>
          <Button onClick={() => setForm({ open: true, carrier: null })} className="h-10 px-4" disabled={!editing.allowed}>
            <Plus aria-hidden="true" />
            {text.add}
          </Button>
        </DisabledReason>
      </div>

      <GlassCard variant="strong" className="flex flex-col gap-0 overflow-hidden p-0">
        <DataTable
          caption={copy.settings.tabs.carriers}
          columns={columns}
          rows={carriers ?? null}
          getRowKey={(carrier) => carrier.id}
          error={resource.error}
          onRetry={resource.retry}
          busy={resource.loading}
          skeletonRows={3}
          emptyMessage={text.empty}
        />
      </GlassCard>

      <CarrierFormDialog
        open={form.open}
        onOpenChange={(open) => setForm((current) => ({ ...current, open }))}
        carrier={form.carrier}
        carriers={carriers ?? []}
      />
      <ConfirmDialog
        open={removing.open}
        onOpenChange={(open) => setRemoving((current) => ({ ...current, open }))}
        title={text.confirm.title}
        description={target ? text.confirm.text(target.name) : ""}
        confirmLabel={text.confirm.action}
        busy={busy}
        onConfirm={confirmRemove}
      />
      <BlockedDialog
        open={blocked.open}
        onOpenChange={(open) => setBlocked((current) => ({ ...current, open }))}
        carrier={blocked.carrier}
      />
    </div>
  );
}
