// Run with `npm test`. The numbers are made up: their check digit is computed
// here, they are not real SIM numbers.
import { test } from "node:test";
import assert from "node:assert/strict";
import { checkIccid, iccidFromScan, luhnValid, stripIccid } from "./iccid.js";

/** Appends the Luhn check digit to a digits-only body. */
function withCheckDigit(body) {
  for (let digit = 0; digit <= 9; digit += 1) {
    if (luhnValid(`${body}${digit}`)) return `${body}${digit}`;
  }
  throw new Error("unreachable");
}

const VALID_19 = withCheckDigit("895401000000001234");
const VALID_20 = withCheckDigit("8925401000000012345");

test("sample numbers have the expected lengths", () => {
  assert.equal(VALID_19.length, 19);
  assert.equal(VALID_20.length, 20);
});

test("luhnValid matches the ISO/IEC 7812 example", () => {
  assert.equal(luhnValid("79927398713"), true);
  assert.equal(luhnValid("79927398710"), false);
  assert.equal(luhnValid("12a4"), false);
});

test("checkIccid accepts 19 and 20 digit numbers", () => {
  assert.deepEqual(checkIccid(VALID_19), { ok: true, iccid: VALID_19 });
  assert.deepEqual(checkIccid(VALID_20), { ok: true, iccid: VALID_20 });
});

test("checkIccid strips spaces and dashes", () => {
  const spaced = VALID_20.replace(/(.{4})/g, "$1 ").trim();
  const dashed = VALID_20.replace(/(.{4})/g, "$1-").replace(/-$/, "");
  assert.equal(stripIccid(spaced), VALID_20);
  assert.deepEqual(checkIccid(spaced), { ok: true, iccid: VALID_20 });
  assert.deepEqual(checkIccid(dashed), { ok: true, iccid: VALID_20 });
});

test("checkIccid rejects the wrong length", () => {
  assert.deepEqual(checkIccid(VALID_19.slice(0, 18)), { ok: false, code: "length" });
  assert.deepEqual(checkIccid(`${VALID_20}0`), { ok: false, code: "length" });
  assert.deepEqual(checkIccid(""), { ok: false, code: "characters" });
});

test("checkIccid rejects a bad check digit", () => {
  const last = Number(VALID_20.at(-1));
  const wrong = `${VALID_20.slice(0, -1)}${(last + 1) % 10}`;
  assert.deepEqual(checkIccid(wrong), { ok: false, code: "checksum" });
});

test("checkIccid rejects numbers that do not start with 89", () => {
  const other = withCheckDigit("9025401000000012345");
  assert.equal(other.length, 20);
  assert.deepEqual(checkIccid(other), { ok: false, code: "prefix" });
});

test("checkIccid rejects letters and other characters", () => {
  assert.deepEqual(checkIccid(`${VALID_19.slice(0, 10)}x${VALID_19.slice(11)}`), { ok: false, code: "characters" });
  assert.deepEqual(checkIccid(null), { ok: false, code: "characters" });
});

test("iccidFromScan returns the form value for a clean barcode", () => {
  assert.deepEqual(iccidFromScan(VALID_20), { ok: true, iccid: VALID_20 });
});

test("iccidFromScan finds the number inside printed text", () => {
  const printed = `SIM 4G\nICCID: ${VALID_20.replace(/(.{4})/g, "$1 ").trim()}\nPUK 12345678`;
  assert.deepEqual(iccidFromScan(printed), { ok: true, iccid: VALID_20 });
});

test("iccidFromScan skips a digit run that fails the check", () => {
  const broken = `${VALID_20.slice(0, -1)}${(Number(VALID_20.at(-1)) + 1) % 10}`;
  const text = `${broken} ${VALID_19}`;
  assert.equal(iccidFromScan(text).iccid, VALID_19);
});

test("iccidFromScan rejects text without a valid number", () => {
  assert.equal(iccidFromScan("hello 12345").ok, false);
  assert.equal(iccidFromScan(undefined).ok, false);
});
