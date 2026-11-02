"use client";

import { useState } from "react";
import { toast } from "sonner";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { FormDialog } from "@/components/shared/form-dialog";
import { referenceData, setSimTier } from "@/lib/data";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

function TierForm({ sim, open, onOpenChange }) {
  const [tier, setTier] = useState(sim.tier);
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    if (tier === sim.tier) return onOpenChange(false);

    setBusy(true);
    try {
      await setSimTier(sim.id, tier);
      toast.success(copy.sims.toasts.tierChanged(copy.tiers[tier]));
      onOpenChange(false);
    } catch {
      toast.error(copy.errors.generic);
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={copy.simProfile.tierTitle}
      submitLabel={copy.actions.save}
      busy={busy}
      onSubmit={handleSubmit}
    >
      <fieldset className="flex flex-col gap-2">
        <legend className="sr-only">{copy.tiers.label}</legend>
        {referenceData.tiers.map(({ name, bundleGb }) => (
          <label
            key={name}
            className={cn(
              "flex h-11 cursor-pointer items-center gap-3 rounded-lg border border-input px-3 text-sm transition-colors duration-150 hover:bg-accent",
              "has-[:checked]:border-foreground has-[:checked]:bg-accent has-[:focus-visible]:ring-3 has-[:focus-visible]:ring-ring/50"
            )}
          >
            <input
              type="radio"
              name="tier"
              value={name}
              checked={tier === name}
              onChange={() => setTier(name)}
              className="size-4 accent-[var(--foreground)]"
            />
            {copy.simProfile.tierOption(copy.tiers[name], bundleGb)}
          </label>
        ))}
      </fieldset>
    </FormDialog>
  );
}

/**
 * Change tier: the four tiers with their data bundles.
 * @param {{ sim: object | null, open: boolean, onOpenChange: (open: boolean) => void }} props
 */
export function TierDialog({ sim, open, onOpenChange }) {
  // A new key on every opening starts from the SIM's current tier.
  const opening = useOpeningKey(open);

  if (!sim) return null;
  return <TierForm key={opening} sim={sim} open={open} onOpenChange={onOpenChange} />;
}
