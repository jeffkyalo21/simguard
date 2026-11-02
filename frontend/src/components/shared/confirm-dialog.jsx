"use client";

import { BusyButton } from "@/components/shared/busy-button";
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
import { useReturnFocus } from "@/hooks/use-return-focus";
import { copy } from "@/lib/copy";

/**
 * Confirmation that names the item. Cancel and Escape back out; focus
 * returns to the opener.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void, title: string,
 *   description: string, confirmLabel: string, busy?: boolean,
 *   onConfirm: () => void }} props
 */
export function ConfirmDialog({ open, onOpenChange, title, description, confirmLabel, busy = false, onConfirm }) {
  const returnFocus = useReturnFocus(open);

  return (
    <Dialog open={open} onOpenChange={(next) => !busy && onOpenChange(next)}>
      <DialogContent className="sm:max-w-sm" showCloseButton={false} onCloseAutoFocus={returnFocus}>
        <DialogHeader>
          <DialogTitle className="text-lg font-semibold">{title}</DialogTitle>
          <DialogDescription>{description}</DialogDescription>
        </DialogHeader>
        <DialogFooter className="mx-0 mb-0 rounded-none border-0 bg-transparent p-0">
          <DialogClose asChild>
            <Button type="button" variant="outline" className="h-10 px-4" disabled={busy}>
              {copy.actions.cancel}
            </Button>
          </DialogClose>
          <BusyButton busy={busy} onClick={onConfirm} className="h-10 px-4">
            {confirmLabel}
          </BusyButton>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
