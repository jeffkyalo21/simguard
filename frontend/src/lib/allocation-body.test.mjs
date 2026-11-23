// Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { allocationBody } from "./allocation-body.js";

test("the handed over by person is sent when one is chosen", () => {
  assert.deepEqual(allocationBody({ simId: "4", employeeId: "7", assignedBy: "9" }), { simId: 4, employeeId: 7, assignedBy: 9 });
});

test("an empty choice leaves assignedBy out of the request", () => {
  for (const empty of ["", null, undefined]) {
    assert.deepEqual(allocationBody({ simId: 4, employeeId: 7, assignedBy: empty }), { simId: 4, employeeId: 7 });
  }
  assert.equal("assignedBy" in allocationBody({ simId: 4, employeeId: 7 }), false);
});
