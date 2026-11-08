"use client";

import { useState } from "react";
import Link from "next/link";
import { toast } from "sonner";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { FormDialog } from "@/components/shared/form-dialog";
import { FormField } from "@/components/shared/form-field";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { ApiError, errorMessage } from "@/lib/api";
import { createSim, setSimTier, updateSim } from "@/lib/data";
import { useCarriers, useTiers } from "@/hooks/use-reference";
import { formatPhone } from "@/lib/format";
import { simFieldErrors } from "@/lib/validation";
import { copy } from "@/lib/copy";

const text = copy.sims.form;

// Error codes from client validation, mapped per field. The backend sends
// sentences, so a field it rejects shows that field's own line (first code
// below), and a duplicate (409) shows the "taken" line.
const messages = {
  phoneNumber: { required: text.errors.phoneRequired, invalid: text.errors.phoneInvalid, taken: text.errors.phoneTaken },
  iccid: { required: text.errors.iccidRequired, tooLong: text.errors.iccidTooLong, taken: text.errors.iccidTaken },
  carrierId: { required: text.errors.operatorRequired, invalid: text.errors.operatorRequired },
  planType: { required: text.errors.planRequired },
  monthlyCost: { invalid: text.errors.costInvalid },
};

const FIELD_ORDER = ["phoneNumber", "iccid", "carrierId", "planType", "monthlyCost"];
const SERVER_CODES = { phoneNumber: "invalid", iccid: "tooLong", carrierId: "invalid", operator: "invalid", planType: "required", monthlyCost: "invalid" };

/** Field messages for a failed save, or null when no field is to blame. */
function serverFieldMessages(error) {
  if (!(error instanceof ApiError) || ![400, 409].includes(error.status)) return null;
  const result = {};
  for (const field of Object.keys(error.errors ?? {})) {
    const target = field === "operator" ? "carrierId" : field;
    const code = error.status === 409 ? "taken" : SERVER_CODES[field];
    const message = messages[target]?.[code];
    if (message) result[target] = message;
  }
  return Object.keys(result).length ? result : null;
}

// Shown in place of the carrier select when there are no carriers.
const ADD_CARRIER_ID = "sim-carrierId-add";

function toMessages(codes) {
  return Object.fromEntries(
    Object.entries(codes).map(([field, code]) => [field, messages[field]?.[code] ?? copy.errors.validation])
  );
}

function initialValues(sim) {
  return {
    phoneNumber: sim ? formatPhone(sim.phoneNumber) : "",
    iccid: sim?.iccid ?? "",
    carrierId: sim?.carrierId == null ? "" : String(sim.carrierId),
    // A new SIM takes its plan type from the tier, like imported SIMs.
    planType: sim ? sim.planType : "",
    monthlyCost: sim ? String(sim.monthlyCost) : "",
  };
}

/**
 * @param {{ id: string, label: string, value: string,
 *   options: { value: string, label: string }[], error?: string,
 *   onChange: (value: string) => void, placeholder?: string,
 *   disabled?: boolean, children?: React.ReactNode }} props
 */
export function SelectField({ id, label, value, options, error, onChange, placeholder = label, disabled, children }) {
  return (
    <FormField id={id} label={label} error={error}>
      {(field) => (
        <>
          <Select value={value} onValueChange={onChange} disabled={disabled}>
            <SelectTrigger {...field} className="h-10 w-full data-[size=default]:h-10">
              <SelectValue placeholder={placeholder} />
            </SelectTrigger>
            <SelectContent position="popper">
              {options.map((option) => (
                <SelectItem key={option.value} value={option.value}>
                  {option.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {children}
        </>
      )}
    </FormField>
  );
}

/** The fields. Keyed by the caller, so each opening starts fresh. */
function SimForm({ sim, open, onOpenChange, onSaved }) {
  const [values, setValues] = useState(() => initialValues(sim));
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const carriers = useCarriers();
  const tierOptions = useTiers().map(({ tier }) => ({ value: tier, label: copy.tiers[tier] ?? tier }));
  const carrierOptions = carriers.map((carrier) => ({ value: String(carrier.id), label: carrier.name }));
  const noCarriers = carrierOptions.length === 0;

  const set = (field) => (value) => setValues((current) => ({ ...current, [field]: value }));

  function focusFirstError(fieldErrors) {
    const first = FIELD_ORDER.find((field) => fieldErrors[field]);
    // The disabled carrier select cannot take focus; its link can.
    const id = first === "carrierId" && noCarriers ? ADD_CARRIER_ID : `sim-${first}`;
    if (first) document.getElementById(id)?.focus();
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const fieldErrors = toMessages(simFieldErrors(values));
    setErrors(fieldErrors);
    if (Object.keys(fieldErrors).length) return focusFirstError(fieldErrors);

    setBusy(true);
    let saved = null;
    try {
      saved = sim ? await updateSim(sim.id, values) : await createSim(values);
      // A new SIM gets the chosen tier, so its allowance and cost apply.
      if (!sim) await setSimTier(saved.id, values.planType);
      const number = formatPhone(saved.phoneNumber);
      toast.success(sim ? copy.sims.toasts.saved(number) : copy.sims.toasts.added(number));
      onSaved?.(saved);
      onOpenChange(false);
    } catch (error) {
      if (saved) {
        // The SIM exists; only the tier failed. Close and say so.
        toast.error(text.tierFailed(formatPhone(saved.phoneNumber)));
        onSaved?.(saved);
        onOpenChange(false);
        return;
      }
      const serverErrors = serverFieldMessages(error);
      if (serverErrors) {
        setErrors(serverErrors);
        focusFirstError(serverErrors);
      } else {
        toast.error(errorMessage(error));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={sim ? text.editTitle : text.addTitle}
      submitLabel={sim ? copy.actions.save : copy.actions.add}
      busy={busy}
      onSubmit={handleSubmit}
    >
      <FormField id="sim-phoneNumber" label={text.phone} error={errors.phoneNumber}>
        {(field) => (
          <Input
            {...field}
            value={values.phoneNumber}
            onChange={(event) => set("phoneNumber")(event.target.value)}
            type="tel"
            inputMode="tel"
            autoComplete="off"
            placeholder={text.phonePlaceholder}
            className="h-10"
          />
        )}
      </FormField>
      <FormField id="sim-iccid" label={text.iccid} error={errors.iccid}>
        {(field) => (
          <Input
            {...field}
            value={values.iccid}
            onChange={(event) => set("iccid")(event.target.value)}
            autoComplete="off"
            placeholder={text.iccidPlaceholder}
            className="h-10"
          />
        )}
      </FormField>
      <div className="grid gap-4 sm:grid-cols-2">
        <SelectField
          id="sim-carrierId"
          label={text.operator}
          value={values.carrierId}
          options={carrierOptions}
          error={errors.carrierId}
          onChange={set("carrierId")}
          placeholder={noCarriers ? text.noCarriers : text.operator}
          disabled={noCarriers}
        >
          {noCarriers && (
            <Link
              id={ADD_CARRIER_ID}
              href="/settings?tab=carriers"
              className="inline-flex min-h-6 w-fit items-center rounded-sm text-sm font-medium underline underline-offset-4 hover:text-foreground"
            >
              {text.addCarrier}
            </Link>
          )}
        </SelectField>
        {sim ? (
          <div className="flex flex-col gap-2">
            <span className="text-sm font-medium">{text.plan}</span>
            <span className="flex h-10 items-center text-sm text-muted-foreground">{copy.tiers[sim.planType] ?? sim.planType}</span>
          </div>
        ) : (
          <SelectField
            id="sim-planType"
            label={text.tier}
            value={values.planType}
            options={tierOptions}
            error={errors.planType}
            onChange={set("planType")}
          />
        )}
      </div>
      <FormField id="sim-monthlyCost" label={text.cost} hint={text.costHint} error={errors.monthlyCost}>
        {(field) => (
          <Input
            {...field}
            value={values.monthlyCost}
            onChange={(event) => set("monthlyCost")(event.target.value)}
            type="number"
            inputMode="decimal"
            min={0}
            step="0.01"
            className="h-10"
          />
        )}
      </FormField>
    </FormDialog>
  );
}

/**
 * Add SIM (no `sim`) or Edit SIM.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void,
 *   sim?: object | null, onSaved?: (sim: object) => void }} props
 */
export function SimFormDialog({ open, onOpenChange, sim = null, onSaved }) {
  // A new key on every opening resets the fields and errors, while closing
  // keeps the key so the exit transition still plays.
  const opening = useOpeningKey(open);

  return (
    <SimForm
      key={`${sim?.id ?? "new"}-${opening}`}
      sim={sim}
      open={open}
      onOpenChange={onOpenChange}
      onSaved={onSaved}
    />
  );
}
