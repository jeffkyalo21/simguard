"use client";

import { BusyButton } from "@/components/shared/busy-button";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { useReturnFocus } from "@/hooks/use-return-focus";
import { copy } from "@/lib/copy";

/**
 * One dialog pattern for forms: title, fields, Cancel and a submit verb.
 * Focus is trapped inside and returns to the opener on close; Escape and
 * Cancel close it.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void, title: string,
 *   submitLabel: string, busy?: boolean, onSubmit: (event: SubmitEvent) => void,
 *   children: React.ReactNode, wide?: boolean }} props
 */
export function FormDialog({ open, onOpenChange, title, submitLabel, busy = false, onSubmit, children, wide }) {
  const returnFocus = useReturnFocus(open);

  return (
    <Dialog open={open} onOpenChange={(next) => !busy && onOpenChange(next)}>
      <DialogContent className={wide ? "sm:max-w-lg" : "sm:max-w-md"} aria-describedby={undefined} onCloseAutoFocus={returnFocus}>
        <form onSubmit={onSubmit} noValidate className="flex flex-col gap-5">
          <DialogHeader>
            <DialogTitle className="text-lg font-semibold">{title}</DialogTitle>
          </DialogHeader>
          <div className="flex flex-col gap-4">{children}</div>
          <DialogFooter className="mx-0 mb-0 rounded-none border-0 bg-transparent p-0">
            <DialogClose asChild>
              <Button type="button" variant="outline" className="h-10 px-4" disabled={busy}>
                {copy.actions.cancel}
              </Button>
            </DialogClose>
            <BusyButton type="submit" busy={busy} className="h-10 px-4">
              {submitLabel}
            </BusyButton>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
