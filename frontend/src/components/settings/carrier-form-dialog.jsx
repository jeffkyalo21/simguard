"use client";

import { useState } from "react";
import { toast } from "sonner";
import { FormDialog } from "@/components/shared/form-dialog";
import { FormField } from "@/components/shared/form-field";
import { Input } from "@/components/ui/input";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { ApiError } from "@/lib/api";
import { addCarrier, renameCarrier } from "@/lib/data";
import { carrierNameError } from "@/lib/validation";
import { copy } from "@/lib/copy";

const text = copy.settings.carriers.form;
const FIELD_ID = "carrier-name";

/** Fields of one opening. Keyed by the caller, so each opening starts fresh. */
function CarrierForm({ open, onOpenChange, carrier, carriers }) {
  const renaming = Boolean(carrier);
  const [name, setName] = useState(carrier?.name ?? "");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  function fail(code) {
    setError(text.errors[code] ?? copy.errors.validation);
    document.getElementById(FIELD_ID)?.focus();
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const code = carrierNameError(name, carriers, carrier?.id);
    if (code) return fail(code);

    setBusy(true);
    try {
      const saved = renaming ? await renameCarrier(carrier.id, name) : await addCarrier(name);
      toast.success(renaming ? copy.settings.carriers.toasts.renamed(saved.name) : copy.settings.carriers.toasts.added(saved.name));
      onOpenChange(false);
    } catch (err) {
      // 409 and 400 both carry errors.name = [code].
      const serverCode = err instanceof ApiError ? err.errors?.name?.[0] : null;
      if (serverCode) fail(serverCode);
      else toast.error(err instanceof ApiError && err.status === 404 ? copy.errors.notFound : copy.errors.generic);
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={renaming ? text.renameTitle : text.addTitle}
      submitLabel={renaming ? text.save : copy.actions.add}
      busy={busy}
      onSubmit={handleSubmit}
    >
      <FormField id={FIELD_ID} label={text.name} error={error}>
        {(field) => (
          <Input
            {...field}
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder={text.placeholder}
            autoComplete="off"
            className="h-10"
          />
        )}
      </FormField>
    </FormDialog>
  );
}

/**
 * Add a carrier, or rename one when `carrier` is given.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void,
 *   carrier?: { id: number, name: string } | null,
 *   carriers: { id: number, name: string }[] }} props
 */
export function CarrierFormDialog({ open, onOpenChange, carrier, carriers }) {
  const opening = useOpeningKey(open);
  if (!opening) return null;

  return <CarrierForm key={opening} open={open} onOpenChange={onOpenChange} carrier={carrier} carriers={carriers} />;
}
