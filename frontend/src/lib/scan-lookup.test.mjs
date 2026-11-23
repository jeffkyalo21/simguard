// Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { findRegisteredSim } from "./scan-lookup.js";
import { iccidFromScan } from "./iccid.js";

const sims = [
  { id: 1, iccid: "8925401000000000011", status: "available", operator: "Safaricom" },
  { id: 2, iccid: "89254010000000000117", status: "assigned", operator: "Airtel" },
];

test("a SIM with exactly the scanned ICCID is found as already in the system", () => {
  assert.equal(findRegisteredSim("8925401000000000011", sims)?.id, 1);
});

test("a longer number that only starts with the ICCID is not a duplicate", () => {
  assert.equal(findRegisteredSim("892540100000000001", sims), null);
});

test("a typed ICCID with spaces around it still matches", () => {
  assert.equal(findRegisteredSim("  89254010000000000117 ", sims)?.id, 2);
});

test("the SIM being edited is not a duplicate of itself", () => {
  assert.equal(findRegisteredSim("8925401000000000011", sims, 1), null);
  assert.equal(findRegisteredSim("89254010000000000117", sims, 1)?.id, 2);
});

test("an empty ICCID or an empty result is not a duplicate", () => {
  assert.equal(findRegisteredSim("", sims), null);
  assert.equal(findRegisteredSim(undefined, sims), null);
  assert.equal(findRegisteredSim("8925401000000000011", []), null);
});

test("a scan that fails the ICCID check is refused with a reason", () => {
  assert.deepEqual(iccidFromScan("12345"), { ok: false, code: "length" });
  assert.equal(iccidFromScan("8925401000000000012").ok, false);
});
