// ICCID rules for the SIM scanner: what a scan or a typed number must look
// like before it is put in a form. Pure functions, no browser APIs.

export const ICCID_PREFIX = "89";
export const ICCID_LENGTHS = [19, 20];

/** Removes spaces and dashes. */
export function stripIccid(input) {
  return String(input ?? "").replace(/[\s-]/g, "");
}

/** Luhn check (ISO/IEC 7812) over every digit of the number, check digit included. */
export function luhnValid(digits) {
  if (!/^\d+$/.test(digits)) return false;
  let sum = 0;
  let double = false;

  for (let index = digits.length - 1; index >= 0; index -= 1) {
    let digit = Number(digits[index]);
    if (double) {
      digit *= 2;
      if (digit > 9) digit -= 9;
    }
    sum += digit;
    double = !double;
  }

  return sum % 10 === 0;
}

/**
 * Checks a scanned or typed ICCID: digits only (after removing spaces and
 * dashes), 19 or 20 digits, starts with 89 and passes the Luhn check.
 * @param {unknown} input
 * @returns {{ ok: true, iccid: string } | { ok: false, code: "characters" | "length" | "prefix" | "checksum" }}
 */
export function checkIccid(input) {
  const value = stripIccid(input);

  if (!/^\d+$/.test(value)) return { ok: false, code: "characters" };
  if (!ICCID_LENGTHS.includes(value.length)) return { ok: false, code: "length" };
  if (!value.startsWith(ICCID_PREFIX)) return { ok: false, code: "prefix" };
  if (!luhnValid(value)) return { ok: false, code: "checksum" };
  return { ok: true, iccid: value };
}

/**
 * Turns the text of a scan into the form value. A barcode normally holds
 * only the number; printed text read by OCR can hold more, so the first
 * number found in it that passes the check wins.
 * @param {unknown} text
 * @returns {{ ok: true, iccid: string } | { ok: false, code: string }}
 */
export function iccidFromScan(text) {
  const whole = checkIccid(text);
  if (whole.ok) return whole;

  // Digits may be grouped with spaces or dashes, and two numbers can sit
  // next to each other, so every 19 or 20 digit window that starts with 89
  // inside a run is tried. Letters are never turned into digits: an "O" in
  // the middle of a number breaks the run, so the number is refused.
  const runs = String(text ?? "").match(/\d(?:[\s-]*\d)+/g) ?? [];
  for (const run of runs) {
    const digits = stripIccid(run);
    for (let start = 0; start < digits.length; start += 1) {
      if (!digits.startsWith(ICCID_PREFIX, start)) continue;
      for (const length of ICCID_LENGTHS) {
        const found = checkIccid(digits.slice(start, start + length));
        if (found.ok) return found;
      }
    }
  }

  return whole;
}
