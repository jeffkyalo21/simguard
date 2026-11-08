"use client";

import { useState } from "react";
import { toast } from "sonner";
import { ConfirmDialog } from "@/components/shared/confirm-dialog";
import { errorMessage } from "@/lib/api";
import { setSimStatus } from "@/lib/data";
import { formatPhone } from "@/lib/format";
import { copy } from "@/lib/copy";

/** Inactive SIMs can become available; others can be set inactive. */
export function nextStatus(sim) {
  return sim.status === "inactive" ? "available" : "inactive";
}

/** An allocated SIM must be returned before its status can change. */
export function canChangeStatus(sim) {
  return !sim.currentAllocation;
}

/**
 * Confirms a status change, naming the SIM.
 * @param {{ sim: object | null, open: boolean, onOpenChange: (open: boolean) => void }} props
 */
export function SimStatusDialog({ sim, open, onOpenChange }) {
  const [busy, setBusy] = useState(false);

  if (!sim) return null;

  const target = nextStatus(sim);
  const number = formatPhone(sim.phoneNumber);
  const inactive = target === "inactive";

  async function confirm() {
    setBusy(true);
    try {
      await setSimStatus(sim.id, target);
      toast.success(inactive ? copy.sims.toasts.inactive(number) : copy.sims.toasts.available(number));
      onOpenChange(false);
    } catch (error) {
      // 409: the SIM was allocated in the meantime.
      toast.error(errorMessage(error, { 409: copy.sims.toasts.statusBlocked }));
    } finally {
      setBusy(false);
    }
  }

  return (
    <ConfirmDialog
      open={open}
      onOpenChange={onOpenChange}
      title={inactive ? copy.sims.confirm.inactiveTitle : copy.sims.confirm.availableTitle}
      description={inactive ? copy.sims.confirm.inactiveText(number) : copy.sims.confirm.availableText(number)}
      confirmLabel={inactive ? copy.sims.setInactive : copy.sims.setAvailable}
      busy={busy}
      onConfirm={confirm}
    />
  );
}
