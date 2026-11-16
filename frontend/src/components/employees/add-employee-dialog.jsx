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
import { errorMessage } from "@/lib/api";
import { createEmployee, listEmployees } from "@/lib/data";
import { employeeFieldErrors, employeeSaveFailure, findByEmail, newEmployeeBody } from "@/lib/employee-picker";
import { copy } from "@/lib/copy";

const text = copy.addEmployee;
const FIELD_ORDER = ["name", "email", "departmentId", "jobRole"];

const CLIENT_MESSAGES = {
  name: { required: text.errors.nameRequired },
  email: { required: text.errors.emailRequired, invalid: text.errors.emailInvalid },
  departmentId: { required: text.errors.departmentRequired },
  jobRole: { required: text.errors.jobRoleRequired },
};

function AddEmployeeForm({ open, onOpenChange, initialName, initialEmail, onCreated }) {
  const [values, setValues] = useState({ name: initialName, email: initialEmail, departmentId: "", jobRole: "" });
  const [errors, setErrors] = useState({});
  const [duplicate, setDuplicate] = useState(null);
  const [busy, setBusy] = useState(false);
  const departments = useDepartments().map(({ id, name }) => ({ value: String(id), label: name }));

  const set = (field) => (value) => {
    setValues((current) => ({ ...current, [field]: value }));
    if (field === "email") setDuplicate(null);
  };

  function showErrors(fieldErrors) {
    setErrors(fieldErrors);
    const first = FIELD_ORDER.find((field) => fieldErrors[field]);
    if (first) document.getElementById(`employee-${first}`)?.focus();
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const codes = employeeFieldErrors(values);
    if (Object.keys(codes).length) {
      return showErrors(Object.fromEntries(Object.entries(codes).map(([field, code]) => [field, CLIENT_MESSAGES[field][code]])));
    }

    setBusy(true);
    setErrors({});
    try {
      const employee = await createEmployee(newEmployeeBody(values));
      toast.success(text.created(employee.name));
      onCreated(employee);
      onOpenChange(false);
    } catch (error) {
      const failure = employeeSaveFailure(error);
      if (failure.kind === "duplicate") {
        // Look the existing employee up so the person can pick them.
        let existing = null;
        try {
          existing = findByEmail((await listEmployees({ search: values.email, pageSize: 5 })).items, values.email);
        } catch {
          // The message still shows; only the shortcut is missing.
        }
        setDuplicate({ employee: existing });
        showErrors({ email: text.duplicate });
      } else if (failure.kind === "fields") {
        showErrors(Object.fromEntries(failure.fields.map((field) => [field, text.errors[field]])));
      } else {
        toast.error(errorMessage(error));
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <FormDialog open={open} onOpenChange={onOpenChange} title={text.title} submitLabel={text.submit} busy={busy} onSubmit={handleSubmit}>
      <FormField id="employee-name" label={text.name} error={errors.name}>
        {(field) => (
          <Input {...field} value={values.name} onChange={(event) => set("name")(event.target.value)} autoComplete="off" className="h-10" />
        )}
      </FormField>
      <FormField id="employee-email" label={text.email} error={errors.email}>
        {(field) => (
          <>
            <Input
              {...field}
              type="email"
              inputMode="email"
              autoCapitalize="none"
              spellCheck={false}
              value={values.email}
              onChange={(event) => set("email")(event.target.value)}
              autoComplete="off"
              className="h-10"
            />
            {duplicate?.employee && (
              <Button
                type="button"
                variant="outline"
                className="h-9 w-fit px-3"
                onClick={() => {
                  onCreated(duplicate.employee);
                  onOpenChange(false);
                }}
              >
                {text.useExisting}
              </Button>
            )}
          </>
        )}
      </FormField>
      <SelectField
        id="employee-departmentId"
        label={text.department}
        value={values.departmentId}
        options={departments}
        error={errors.departmentId}
        onChange={set("departmentId")}
        placeholder={text.departmentPlaceholder}
      />
      <FormField id="employee-jobRole" label={text.jobRole} error={errors.jobRole}>
        {(field) => (
          <Input {...field} value={values.jobRole} onChange={(event) => set("jobRole")(event.target.value)} autoComplete="off" className="h-10" />
        )}
      </FormField>
    </FormDialog>
  );
}

/**
 * Adds an employee from the allocation form. `onCreated` also receives the
 * existing employee when a duplicate email is resolved by picking them.
 * @param {{ open: boolean, onOpenChange: (open: boolean) => void,
 *   initialName?: string, initialEmail?: string, onCreated: (employee: object) => void }} props
 */
export function AddEmployeeDialog({ open, onOpenChange, initialName = "", initialEmail = "", onCreated }) {
  const opening = useOpeningKey(open);
  if (!opening) return null;

  return <AddEmployeeForm key={opening} open={open} onOpenChange={onOpenChange} initialName={initialName} initialEmail={initialEmail} onCreated={onCreated} />;
}
