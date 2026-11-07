"use client";

import { useState } from "react";
import { toast } from "sonner";
import { FormDialog } from "@/components/shared/form-dialog";
import { FormField } from "@/components/shared/form-field";
import { SelectField } from "@/components/sims/sim-form-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { useDepartments } from "@/hooks/use-reference";
import { useResource } from "@/hooks/use-resource";
import { ApiError } from "@/lib/api";
import { createAllocation, listEmployees, listSims } from "@/lib/data";
import { formatPhone, todayDay } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.allocations.form;
const FIELD_ORDER = ["simId", "departmentId", "employeeId", "assignmentDate"];

// Error codes from the data layer, mapped per field.
const messages = {
  simId: { required: text.errors.simRequired, invalid: text.errors.simUnavailable },
  employeeId: { required: text.errors.employeeRequired, invalid: text.errors.employeeRequired },
  assignmentDate: { invalid: text.errors.dateRequired, future: text.errors.dateFuture },
};

function validate(values, today) {
  const errors = {};
  if (!values.simId) errors.simId = text.errors.simRequired;
  if (!values.departmentId) errors.departmentId = text.errors.departmentRequired;
  else if (!values.employeeId) errors.employeeId = text.errors.employeeRequired;
  if (!values.assignmentDate) errors.assignmentDate = text.errors.dateRequired;
  else if (values.assignmentDate > today) errors.assignmentDate = text.errors.dateFuture;
  return errors;
}

function focusFirstError(errors) {
  const first = FIELD_ORDER.find((field) => errors[field]);
  if (first) document.getElementById(`allocation-${first}`)?.focus();
}

/** The fields. Keyed by the caller, so each opening starts fresh. */
function AllocationForm({ open, onOpenChange }) {
  const today = todayDay();
  const [values, setValues] = useState({ simId: "", departmentId: "", employeeId: "", assignmentDate: today });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);

  // Only available SIMs can be allocated (the API answers 409 otherwise).
  const sims = useResource(() => listSims({ status: "available", pageSize: 100 }), ["available"]);
  const employees = useResource(
    () => (values.departmentId ? listEmployees({ departmentId: values.departmentId, pageSize: 100 }) : Promise.resolve(null)),
    [values.departmentId]
  );

  const departmentOptions = useDepartments().map(({ id, name }) => ({ value: String(id), label: name }));
  const simOptions = (sims.data?.items ?? []).map((sim) => ({ value: String(sim.id), label: formatPhone(sim.phoneNumber) }));
  // The API lists employees by id; the form shows them by name.
  const employeeOptions = (employees.data?.items ?? [])
    .map((employee) => ({ value: String(employee.id), label: employee.name }))
    .sort((a, b) => a.label.localeCompare(b.label));

  const simPlaceholder = sims.error
    ? text.simPlaceholder
    : !sims.data ? text.loading : simOptions.length ? text.simPlaceholder : text.noSims;
  const employeePlaceholder = !values.departmentId
    ? text.employeeFirst
    : employees.error
      ? text.employeePlaceholder
      : !employees.data ? text.loading : employeeOptions.length ? text.employeePlaceholder : text.noEmployees;

  const set = (field) => (value) =>
    setValues((current) => ({ ...current, [field]: value, ...(field === "departmentId" && { employeeId: "" }) }));

  async function handleSubmit(event) {
    event.preventDefault();
    const fieldErrors = validate(values, today);
    setErrors(fieldErrors);
    if (Object.keys(fieldErrors).length) return focusFirstError(fieldErrors);

    setBusy(true);
    try {
      const allocation = await createAllocation({
        simId: values.simId,
        employeeId: values.employeeId,
        assignmentDate: values.assignmentDate,
      });
      toast.success(copy.allocations.toasts.created(formatPhone(allocation.simPhoneNumber), allocation.employeeName));
      onOpenChange(false);
    } catch (error) {
      let serverErrors = {};
      if (error instanceof ApiError && error.status === 409) {
        serverErrors = { simId: text.errors.simUnavailable };
      } else if (error instanceof ApiError && error.status === 400) {
        serverErrors = Object.fromEntries(
          Object.entries(error.errors).map(([field, codes]) => [field, messages[field]?.[codes[0]] ?? copy.errors.validation])
        );
      }
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

  const loadFailed = sims.error || employees.error;

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={text.title}
      submitLabel={text.submit}
      busy={busy}
      onSubmit={handleSubmit}
    >
      {loadFailed && (
        <div role="alert" className="flex flex-wrap items-center justify-between gap-2 text-sm">
          <span className="text-risk-flagged-foreground">{text.loadError}</span>
          <Button
            type="button"
            variant="outline"
            className="h-9 px-3"
            onClick={() => {
              if (sims.error) sims.retry();
              if (employees.error) employees.retry();
            }}
          >
            {copy.actions.tryAgain}
          </Button>
        </div>
      )}
      <SelectField
        id="allocation-simId"
        label={text.sim}
        value={values.simId}
        options={simOptions}
        error={errors.simId}
        onChange={set("simId")}
        placeholder={simPlaceholder}
        disabled={!simOptions.length}
      />
      <SelectField
        id="allocation-departmentId"
        label={text.department}
        value={values.departmentId}
        options={departmentOptions}
        error={errors.departmentId}
        onChange={set("departmentId")}
        placeholder={text.departmentPlaceholder}
      />
      <SelectField
        id="allocation-employeeId"
        label={text.employee}
        value={values.employeeId}
        options={employeeOptions}
        error={errors.employeeId}
        onChange={set("employeeId")}
        placeholder={employeePlaceholder}
        disabled={!employeeOptions.length}
      />
      <FormField id="allocation-assignmentDate" label={text.startDate} error={errors.assignmentDate}>
        {(field) => (
          <Input
            {...field}
            type="date"
            max={today}
            value={values.assignmentDate}
            onChange={(event) => set("assignmentDate")(event.target.value)}
            className="h-10"
          />
        )}
      </FormField>
    </FormDialog>
  );
}

/**
 * New allocation: an available SIM, an employee and a start date.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void }} props
 */
export function AllocationFormDialog({ open, onOpenChange }) {
  // A new key on every opening resets the fields, while closing keeps the
  // key so the exit transition still plays.
  // Nothing loads until the first opening.
  const opening = useOpeningKey(open);
  if (!opening) return null;

  return <AllocationForm key={opening} open={open} onOpenChange={onOpenChange} />;
}
