"use client";

import { useEffect, useState } from "react";
import { ScanLine } from "lucide-react";
import { toast } from "sonner";
import { AddEmployeeDialog } from "@/components/employees/add-employee-dialog";
import { EmployeePicker } from "@/components/employees/employee-picker";
import { ScanSimDialog } from "@/components/scan/scan-sim-dialog";
import { FormDialog } from "@/components/shared/form-dialog";
import { FormField } from "@/components/shared/form-field";
import { SelectField, SimFormDialog } from "@/components/sims/sim-form-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useOpeningKey } from "@/hooks/use-opening-key";
import { usePermission } from "@/hooks/use-permission";
import { useResource } from "@/hooks/use-resource";
import { ApiError, errorMessage } from "@/lib/api";
import { createAllocation, listEmployees, listSims } from "@/lib/data";
import { formatPhone } from "@/lib/format";
import { classifyScannedSim, simFieldValue, withExtraSim } from "@/lib/scan-lookup";
import { copy } from "@/lib/copy";

const text = copy.allocations.form;
const FIELD_ORDER = ["simId", "employeeId"];
const SEARCH_DELAY_MS = 250;

// The backend answers 400 with sentences per field; each field shows its
// own line instead.
const serverMessages = {
  simId: text.errors.simUnavailable,
  employeeId: text.errors.employeeRequired,
};

function validate(values) {
  const errors = {};
  if (!values.simId) errors.simId = text.errors.simRequired;
  if (!values.employeeId) errors.employeeId = text.errors.employeeRequired;
  return errors;
}

function focusFirstError(errors) {
  const first = FIELD_ORDER.find((field) => errors[field]);
  if (first) document.getElementById(`allocation-${first}`)?.focus();
}

/** The fields. Keyed by the caller, so each opening starts fresh. */
function AllocationForm({ open, onOpenChange }) {
  const [values, setValues] = useState({ simId: "", employeeId: "", assignedBy: "" });
  // The picked employee, shown in the summary under the field.
  const [employee, setEmployee] = useState(null);
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  // The person who hands the SIM over is any employee: search by name.
  const [assignerSearch, setAssignerSearch] = useState("");
  const [assignerQuery, setAssignerQuery] = useState("");
  // Scanning: the dialogs, the SIM found by a scan, and what to tell the person.
  const [scanOpen, setScanOpen] = useState(false);
  const [registerIccid, setRegisterIccid] = useState(null);
  const [extraSim, setExtraSim] = useState(null);
  const [scanNote, setScanNote] = useState(null);
  const [adding, setAdding] = useState(null);
  const addEmployee = usePermission("employees.edit");
  const editSims = usePermission("sims.edit");

  useEffect(() => {
    const timer = setTimeout(() => setAssignerQuery(assignerSearch.trim()), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [assignerSearch]);

  // Only available SIMs can be allocated (the API answers 409 otherwise).
  const sims = useResource(() => listSims({ status: "available", pageSize: 100 }), ["available"]);

  const assigners = useResource(
    () => (assignerQuery.length >= 2 ? listEmployees({ search: assignerQuery, pageSize: 20 }) : Promise.resolve(null)),
    [assignerQuery]
  );
  const assignerOptions = (assigners.data?.items ?? [])
    .map((person) => ({
      value: String(person.id),
      label: person.departmentName ? `${person.name}, ${person.departmentName}` : person.name,
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
  const assignerPlaceholder = assignerQuery.length < 2
    ? text.assignerFirst
    : assigners.error
      ? text.assignerPlaceholder
      : !assigners.data ? text.loading : assignerOptions.length ? text.assignerPlaceholder : text.noAssigners;

  const listed = (sims.data?.items ?? []).map((sim) => ({ value: String(sim.id), label: formatPhone(sim.phoneNumber) }));
  const simOptions = extraSim ? withExtraSim(listed, extraSim, formatPhone(extraSim.phoneNumber)) : listed;

  const simPlaceholder = sims.error
    ? text.simPlaceholder
    : !sims.data ? text.loading : simOptions.length ? text.simPlaceholder : text.noSims;

  const set = (field) => (value) => setValues((current) => ({ ...current, [field]: value }));

  function chooseEmployee(person) {
    setEmployee(person);
    setValues((current) => ({ ...current, employeeId: person ? String(person.id) : "" }));
    setErrors((current) => ({ ...current, employeeId: undefined }));
  }

  function chooseSim(sim) {
    setExtraSim(sim);
    set("simId")(simFieldValue(sim));
    setErrors((current) => ({ ...current, simId: undefined }));
  }

  // A scanned ICCID selects the registered, available SIM that has it.
  async function handleScanned(iccid) {
    setScanNote(null);
    try {
      const found = await listSims({ q: iccid, pageSize: 5 });
      const { kind, sim } = classifyScannedSim(iccid, found.items);
      const number = sim ? formatPhone(sim.phoneNumber) : "";

      if (kind === "available") {
        chooseSim(sim);
        setScanNote({ kind, text: text.scanPicked(number) });
      } else if (kind === "allocated") {
        setScanNote({ kind, text: text.scanAllocated(number, sim.currentAllocation?.employeeName) });
      } else if (kind === "inactive") {
        setScanNote({ kind, text: text.scanInactive(number) });
      } else {
        setScanNote({ kind, text: text.scanUnregistered, iccid });
      }
    } catch {
      setScanNote({ kind: "error", text: text.scanLookupFailed });
    }
  }

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

  const loadFailed = sims.error || assigners.error;

  return (
    <>
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
                if (assigners.error) assigners.retry();
              }}
            >
              {copy.actions.tryAgain}
            </Button>
          </div>
        )}
        <div className="flex flex-col gap-2">
          <div className="flex items-start gap-2">
            <div className="min-w-0 flex-1">
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
            </div>
            {/* The label above the select is 14px high with an 8px gap. */}
            <Button
              type="button"
              variant="outline"
              className="mt-[1.375rem] h-10 shrink-0 px-3"
              aria-label={text.scanSimLabel}
              onClick={() => setScanOpen(true)}
            >
              <ScanLine aria-hidden="true" />
              {text.scanSim}
            </Button>
          </div>
          {scanNote && (
            <p role="status" className={scanNote.kind === "available" ? "text-sm text-muted-foreground" : "text-sm text-risk-flagged-foreground"}>
              {scanNote.text}
              {scanNote.kind === "unregistered" && editSims.allowed && (
                <>
                  {" "}
                  <button
                    type="button"
                    className="rounded-sm font-medium underline underline-offset-4"
                    onClick={() => setRegisterIccid(scanNote.iccid)}
                  >
                    {text.registerSim}
                  </button>
                </>
              )}
            </p>
          )}
        </div>
        <EmployeePicker
          id="allocation-employeeId"
          label={text.employee}
          value={employee}
          error={errors.employeeId}
          onChange={chooseEmployee}
          onAdd={addEmployee.allowed ? (query) => setAdding(query) : undefined}
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
          onChange={set("assignedBy")}
          placeholder={assignerPlaceholder}
          disabled={!assignerOptions.length}
        />
      </FormDialog>

      {/* Siblings of the form, so their submit events never reach it. */}
      <ScanSimDialog open={scanOpen} onOpenChange={setScanOpen} onResult={handleScanned} />
      <SimFormDialog
        open={registerIccid !== null}
        onOpenChange={(next) => !next && setRegisterIccid(null)}
        initialIccid={registerIccid ?? ""}
        onSaved={(sim) => {
          chooseSim(sim);
          setScanNote({ kind: "available", text: text.scanPicked(formatPhone(sim.phoneNumber)) });
        }}
      />
      <AddEmployeeDialog
        open={adding !== null}
        onOpenChange={(next) => !next && setAdding(null)}
        initialName={adding && !adding.includes("@") ? adding : ""}
        initialEmail={adding?.includes("@") ? adding : ""}
        onCreated={chooseEmployee}
      />
    </>
  );
}

/**
 * New allocation: an available SIM (picked or scanned), an employee (found by
 * search, or added on the spot) and who handed it over. The allocation starts
 * now.
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
