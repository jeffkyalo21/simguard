"use client";

import { useState } from "react";
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
import { ApiError } from "@/lib/api";
import { createSim, referenceData, updateSim } from "@/lib/data";
import { formatPhone } from "@/lib/format";
import { simFieldErrors } from "@/lib/validation";
import { copy } from "@/lib/copy";

const text = copy.sims.form;

// Error codes from validation and the data layer, mapped per field.
const messages = {
  phoneNumber: { required: text.errors.phoneRequired, invalid: text.errors.phoneInvalid, taken: text.errors.phoneTaken },
  iccid: { required: text.errors.iccidRequired, tooLong: text.errors.iccidTooLong, taken: text.errors.iccidTaken },
  operator: { required: text.errors.operatorRequired },
  planType: { required: text.errors.planRequired },
  monthlyCost: { invalid: text.errors.costInvalid },
};

const FIELD_ORDER = ["phoneNumber", "iccid", "operator", "planType", "monthlyCost"];

function toMessages(codes) {
  return Object.fromEntries(
    Object.entries(codes).map(([field, code]) => [field, messages[field]?.[code] ?? copy.errors.validation])
  );
}

function initialValues(sim) {
  return {
    phoneNumber: sim ? formatPhone(sim.phoneNumber) : "",
    iccid: sim?.iccid ?? "",
    operator: sim?.operator ?? "",
    planType: sim?.planType ?? "",
    monthlyCost: sim ? String(sim.monthlyCost) : "",
  };
}

function SelectField({ id, label, value, options, error, onChange }) {
  return (
    <FormField id={id} label={label} error={error}>
      {(field) => (
        <Select value={value} onValueChange={onChange}>
          <SelectTrigger {...field} className="h-10 w-full data-[size=default]:h-10">
            <SelectValue placeholder={label} />
          </SelectTrigger>
          <SelectContent position="popper">
            {options.map((option) => (
              <SelectItem key={option} value={option}>
                {option}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
    </FormField>
  );
}

/** The fields. Keyed by the caller, so each opening starts fresh. */
function SimForm({ sim, open, onOpenChange, onSaved }) {
  const [values, setValues] = useState(() => initialValues(sim));
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  const set = (field) => (value) => setValues((current) => ({ ...current, [field]: value }));

  function focusFirstError(fieldErrors) {
    const first = FIELD_ORDER.find((field) => fieldErrors[field]);
    if (first) document.getElementById(`sim-${first}`)?.focus();
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const fieldErrors = toMessages(simFieldErrors(values));
    setErrors(fieldErrors);
    if (Object.keys(fieldErrors).length) return focusFirstError(fieldErrors);

    setBusy(true);
    try {
      const saved = sim ? await updateSim(sim.id, values) : await createSim(values);
      const number = formatPhone(saved.phoneNumber);
      toast.success(sim ? copy.sims.toasts.saved(number) : copy.sims.toasts.added(number));
      onSaved?.(saved);
      onOpenChange(false);
    } catch (error) {
      const codes = error instanceof ApiError ? error.errors : {};
      const serverErrors = toMessages(Object.fromEntries(Object.entries(codes).map(([field, list]) => [field, list[0]])));
      if (Object.keys(serverErrors).length) {
        setErrors(serverErrors);
        focusFirstError(serverErrors);
      } else {
        toast.error(copy.errors.generic);
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
          id="sim-operator"
          label={text.operator}
          value={values.operator}
          options={referenceData.operators}
          error={errors.operator}
          onChange={set("operator")}
        />
        <SelectField
          id="sim-planType"
          label={text.plan}
          value={values.planType}
          options={referenceData.planTypes}
          error={errors.planType}
          onChange={set("planType")}
        />
      </div>
      <FormField id="sim-monthlyCost" label={text.cost} error={errors.monthlyCost}>
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
