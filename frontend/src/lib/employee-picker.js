// Pure rules of the allocation form's employee field and the add-employee
// dialog: what a result row shows, what the summary shows, and how a failed
// save maps to the form. No React or browser APIs, so `node --test` runs them.
import { can } from "@/lib/permissions";
import { isEmail } from "@/lib/validation";

/** Letters needed before the API is asked (same as the "handed over by" search). */
export const MIN_SEARCH_LENGTH = 2;

/** True when a typed query is long enough to search. */
export function canSearch(query) {
  return String(query ?? "").trim().length >= MIN_SEARCH_LENGTH;
}

/** A result row: the name, then department and job role. */
export function employeeOption(employee) {
  return {
    id: String(employee.id),
    name: employee.name,
    detail: [employee.departmentName, employee.jobRole].filter(Boolean).join(", "),
  };
}

/** Lines of the read-only summary under the field, in display order. */
export function employeeSummary(employee) {
  return [
    ["name", employee.name],
    ["email", employee.email],
    ["department", employee.departmentName],
    ["jobRole", employee.jobRole],
  ].map(([key, value]) => ({ key, value: value || "" }));
}

/** Only roles that may write employees see "Add new employee". */
export function canAddEmployee(role) {
  return can(role, "employees.edit");
}

/**
 * Client checks that mirror the API (EmployeeRequest). Codes per field:
 * required or invalid.
 * @param {{ name?: string, email?: string, departmentId?: string | number, jobRole?: string }} values
 */
export function employeeFieldErrors(values) {
  const errors = {};
  if (!values.name?.trim()) errors.name = "required";
  const email = values.email?.trim() ?? "";
  if (!email) errors.email = "required";
  else if (!isEmail(email)) errors.email = "invalid";
  if (!values.departmentId) errors.departmentId = "required";
  if (!values.jobRole?.trim()) errors.jobRole = "required";
  return errors;
}

/** The POST /employees body. */
export function newEmployeeBody(values) {
  return {
    name: values.name.trim(),
    email: values.email.trim(),
    departmentId: Number(values.departmentId),
    jobRole: values.jobRole.trim(),
  };
}

/**
 * How a failed save is shown: a duplicate email (409) offers the existing
 * employee, a 400 marks the fields the API named, anything else is a toast.
 * @param {{ status?: number, errors?: Record<string, unknown> } | null | undefined} error
 * @returns {{ kind: "duplicate" } | { kind: "fields", fields: string[] } | { kind: "other" }}
 */
export function employeeSaveFailure(error) {
  if (error?.status === 409) return { kind: "duplicate" };
  if (error?.status === 400) {
    const known = ["name", "email", "departmentId", "jobRole"];
    const fields = Object.keys(error.errors ?? {}).filter((field) => known.includes(field));
    if (fields.length) return { kind: "fields", fields };
  }
  return { kind: "other" };
}

/** The employee whose email is exactly `email` (case does not matter), or null. */
export function findByEmail(employees, email) {
  const wanted = String(email ?? "").trim().toLowerCase();
  return employees.find((employee) => employee.email?.toLowerCase() === wanted) ?? null;
}
