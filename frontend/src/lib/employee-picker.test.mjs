// Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  canAddEmployee,
  canSearch,
  employeeFieldErrors,
  employeeOption,
  employeeSaveFailure,
  employeeSummary,
  findByEmail,
  newEmployeeBody,
} from "./employee-picker.js";
import { classifyScannedSim, simFieldValue, withExtraSim } from "./scan-lookup.js";
import { can, deniedReason } from "./permissions.js";
import { ROLES } from "./auth/core.js";

const ada = { id: 7, name: "Ada Mwangi", email: "ada@example.test", departmentId: 2, departmentName: "Field Sales", jobRole: "Team lead" };

test("search starts at two letters", () => {
  assert.equal(canSearch(""), false);
  assert.equal(canSearch(" a "), false);
  assert.equal(canSearch("ad"), true);
  assert.equal(canSearch(undefined), false);
});

test("a result row shows name, department and job role", () => {
  assert.deepEqual(employeeOption(ada), { id: "7", name: "Ada Mwangi", detail: "Field Sales, Team lead" });
  assert.equal(employeeOption({ ...ada, departmentName: "" }).detail, "Team lead");
});

test("the summary lists name, email, department and job role", () => {
  assert.deepEqual(employeeSummary(ada).map((row) => row.key), ["name", "email", "department", "jobRole"]);
  assert.equal(employeeSummary(ada)[1].value, "ada@example.test");
});

test("add employee: required fields and the email shape", () => {
  assert.deepEqual(employeeFieldErrors({}), { name: "required", email: "required", departmentId: "required", jobRole: "required" });
  assert.deepEqual(employeeFieldErrors({ name: "A", email: "nope", departmentId: "2", jobRole: "Driver" }), { email: "invalid" });
  assert.deepEqual(employeeFieldErrors({ name: "A", email: "a@b.co", departmentId: "2", jobRole: "Driver" }), {});
  assert.equal(employeeFieldErrors({ name: "  ", email: "a@b.co", departmentId: "2", jobRole: "x" }).name, "required");
});

test("add employee: the body is trimmed and the department is a number", () => {
  assert.deepEqual(newEmployeeBody({ name: " Ada ", email: " ada@example.test ", departmentId: "2", jobRole: " Lead " }), {
    name: "Ada",
    email: "ada@example.test",
    departmentId: 2,
    jobRole: "Lead",
  });
});

test("add employee: a failed save is a duplicate, field errors or something else", () => {
  assert.deepEqual(employeeSaveFailure({ status: 409 }), { kind: "duplicate" });
  assert.deepEqual(employeeSaveFailure({ status: 400, errors: { email: ["x"], other: ["y"] } }), { kind: "fields", fields: ["email"] });
  assert.deepEqual(employeeSaveFailure({ status: 400, errors: {} }), { kind: "other" });
  assert.deepEqual(employeeSaveFailure({ status: 500 }), { kind: "other" });
  assert.deepEqual(employeeSaveFailure(null), { kind: "other" });
});

test("a duplicate email finds the existing employee, ignoring case", () => {
  assert.equal(findByEmail([ada], " ADA@example.test ")?.id, 7);
  assert.equal(findByEmail([ada], "other@example.test"), null);
});

test("only the Security Officer may add an employee", () => {
  assert.equal(canAddEmployee(ROLES.securityOfficer), true);
  assert.equal(canAddEmployee(ROLES.auditor), false);
  assert.equal(canAddEmployee(ROLES.admin), false);
  assert.equal(canAddEmployee(undefined), false);
});

test("role rules for the allocation form", () => {
  assert.equal(can(ROLES.securityOfficer, "allocations.edit"), true);
  assert.equal(can(ROLES.securityOfficer, "sims.edit"), true);
  assert.equal(can(ROLES.auditor, "allocations.edit"), false);
  assert.equal(can(ROLES.auditor, "sims.edit"), false);
  assert.equal(deniedReason(ROLES.securityOfficer, "employees.edit"), null);
  assert.notEqual(deniedReason(ROLES.auditor, "employees.edit"), null);
});

test("a scanned ICCID is classified for the allocation form", () => {
  const sims = [
    { id: 1, iccid: "8900000000000000001", status: "available" },
    { id: 2, iccid: "8900000000000000002", status: "assigned" },
    { id: 3, iccid: "8900000000000000003", status: "inactive" },
  ];
  assert.equal(classifyScannedSim("8900000000000000001", sims).kind, "available");
  assert.equal(classifyScannedSim("8900000000000000002", sims).kind, "allocated");
  assert.equal(classifyScannedSim("8900000000000000003", sims).kind, "inactive");
  assert.deepEqual(classifyScannedSim("8900000000000000009", sims), { kind: "unregistered", sim: null });
  // A search hit on the phone number is not a match on the ICCID.
  assert.equal(classifyScannedSim("8900000000000000001", [{ id: 9, iccid: "other", status: "available" }]).kind, "unregistered");
});

test("the SIM field value is the id, and a missing option is added", () => {
  assert.equal(simFieldValue({ id: 12 }), "12");
  const options = [{ value: "1", label: "A" }];
  assert.deepEqual(withExtraSim(options, { id: 12 }, "B"), [{ value: "12", label: "B" }, { value: "1", label: "A" }]);
  assert.equal(withExtraSim(options, { id: 1 }, "A"), options);
});
