"use client";

import { useEffect, useState } from "react";
import { toast } from "sonner";
import { FormDialog } from "@/components/shared/form-dialog";
import { FormField } from "@/components/shared/form-field";
import { SelectField } from "@/components/sims/sim-form-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { useDepartments } from "@/hooks/use-reference";
import { useResource } from "@/hooks/use-resource";
import { ApiError, errorMessage } from "@/lib/api";
import { createAllocation, listEmployees, listSims } from "@/lib/data";
import { formatPhone } from "@/lib/format";
import { copy } from "@/lib/copy";

const text = copy.allocations.form;
const FIELD_ORDER = ["simId", "departmentId", "employeeId", "assignedBy"];
const SEARCH_DELAY_MS = 250;

// The backend answers 400 with sentences per field; each field shows its
// own line instead.
const serverMessages = {
  simId: text.errors.simUnavailable,
  employeeId: text.errors.employeeRequired,
  assignedBy: text.errors.assignerRequired,
};

function validate(values) {
  const errors = {};
  if (!values.simId) errors.simId = text.errors.simRequired;
  if (!values.departmentId) errors.departmentId = text.errors.departmentRequired;
  else if (!values.employeeId) errors.employeeId = text.errors.employeeRequired;
  if (!values.assignedBy) errors.assignedBy = text.errors.assignerRequired;
  return errors;
}

function focusFirstError(errors) {
  const first = FIELD_ORDER.find((field) => errors[field]);
  if (first) document.getElementById(`allocation-${first}`)?.focus();
}

/** The fields. Keyed by the caller, so each opening starts fresh. */
function AllocationForm({ open, onOpenChange }) {
  const [values, setValues] = useState({ simId: "", departmentId: "", employeeId: "", assignedBy: "" });
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  // The person who hands the SIM over is any employee: search by name.
  const [assignerSearch, setAssignerSearch] = useState("");
  const [assignerQuery, setAssignerQuery] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setAssignerQuery(assignerSearch.trim()), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [assignerSearch]);

  // Only available SIMs can be allocated (the API answers 409 otherwise).
  const sims = useResource(() => listSims({ status: "available", pageSize: 100 }), ["available"]);
  const employees = useResource(
    () => (values.departmentId ? listEmployees({ departmentId: values.departmentId, pageSize: 100 }) : Promise.resolve(null)),
    [values.departmentId]
  );

  const assigners = useResource(
    () => (assignerQuery.length >= 2 ? listEmployees({ search: assignerQuery, pageSize: 20 }) : Promise.resolve(null)),
    [assignerQuery]
  );
  const assignerOptions = (assigners.data?.items ?? [])
    .map((employee) => ({
      value: String(employee.id),
      label: employee.departmentName ? `${employee.name}, ${employee.departmentName}` : employee.name,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
  const assignerPlaceholder = assignerQuery.length < 2
    ? text.assignerFirst
    : assigners.error
      ? text.assignerPlaceholder
      : !assigners.data ? text.loading : assignerOptions.length ? text.assignerPlaceholder : text.noAssigners;

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
    const fieldErrors = validate(values);
    setErrors(fieldErrors);
    if (Object.keys(fieldErrors).length) return focusFirstError(fieldErrors);

    setBusy(true);
    try {
      const allocation = await createAllocation({
        simId: values.simId,
        employeeId: values.employeeId,
        assignedBy: values.assignedBy,
      });
      toast.success(copy.allocations.toasts.created(formatPhone(allocation.simPhoneNumber), allocation.employeeName));
      onOpenChange(false);
    } catch (error) {
      let serverErrors = {};
      if (error instanceof ApiError && error.status === 409) {
        // The SIM was allocated or deactivated after the list loaded; the
        // list reloads (every 409 refreshes the screens).
        serverErrors = { simId: text.errors.simUnavailable };
      } else if (error instanceof ApiError && error.status === 400) {
        serverErrors = Object.fromEntries(
          Object.keys(error.errors).filter((field) => serverMessages[field]).map((field) => [field, serverMessages[field]])
        );
      }
      if (Object.keys(serverErrors).length) {
        setErrors(serverErrors);
        focusFirstError(serverErrors);
      } else {
        toast.error(errorMessage(error));
      }
    } finally {
      setBusy(false);
    }
  }

  const loadFailed = sims.error || employees.error || assigners.error;

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
              if (assigners.error) assigners.retry();
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
      <FormField id="allocation-assignerSearch" label={text.assignerSearch} hint={text.assignerHint}>
        {(field) => (
          <Input
            {...field}
            type="search"
            value={assignerSearch}
            onChange={(event) => {
              setAssignerSearch(event.target.value);
              set("assignedBy")("");
            }}
            placeholder={text.assignerSearchPlaceholder}
            autoComplete="off"
            className="h-10"
          />
        )}
      </FormField>
      <SelectField
        id="allocation-assignedBy"
        label={text.assigner}
        value={values.assignedBy}
        options={assignerOptions}
        error={errors.assignedBy}
        onChange={set("assignedBy")}
        placeholder={assignerPlaceholder}
        disabled={!assignerOptions.length}
      />
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
