// Client-side mirrors of backend rules, so forms can validate before submit.

const KENYA_E164 = /^\+254[17][0-9]{8}$/;
const OTHER_E164 = /^\+[1-9][0-9]{7,14}$/;

function compact(input) {
  return input.replace(/[\s\-.()]/g, "");
}

/**
 * Port of PhoneNumbers.Normalize in the API. Accepts 0712345678,
 * 254712345678, +254712345678 and 00254712345678 (spaces, dashes, dots and
 * brackets ignored). Returns the E.164 form, or null when invalid.
 * @param {string | null | undefined} input
 */
export function normalizePhone(input) {
  if (!input || !input.trim()) return null;

  const value = compact(input);
  let candidate;

  if (value.startsWith("+")) candidate = value;
  else if (value.startsWith("00")) candidate = `+${value.slice(2)}`;
  else if (value.startsWith("254")) candidate = `+${value}`;
  else if (value.startsWith("0")) candidate = `+254${value.slice(1)}`;
  else return null;

  if (candidate.startsWith("+254")) {
    return KENYA_E164.test(candidate) ? candidate : null;
  }

  return OTHER_E164.test(candidate) ? candidate : null;
}

/** Port of PhoneNumbers.ToSearchTerm: lets "0712" find "+254712...". */
export function phoneSearchTerm(input) {
  if (!input || !input.trim()) return null;

  const value = compact(input);

  if (value.startsWith("00")) return `+${value.slice(2)}`;
  if (value.startsWith("0")) return `+254${value.slice(1)}`;
  if (value.startsWith("254")) return `+${value}`;
  return value;
}

export const ICCID_MAX_LENGTH = 30;

/**
 * Field rules of the API's SimRequest (duplicates are checked server side).
 * Returns error codes per field: required, invalid or tooLong.
 * @param {{ phoneNumber?: string, iccid?: string, carrierId?: string | number,
 *   planType?: string, monthlyCost?: string | number }} values
 * @returns {Record<string, string>}
 */
export function simFieldErrors(values) {
  const errors = {};
  const iccid = values.iccid?.trim() ?? "";
  const cost = values.monthlyCost === "" || values.monthlyCost == null ? NaN : Number(values.monthlyCost);

  if (!values.phoneNumber?.trim()) errors.phoneNumber = "required";
  else if (!normalizePhone(values.phoneNumber)) errors.phoneNumber = "invalid";
  if (!iccid) errors.iccid = "required";
  else if (iccid.length > ICCID_MAX_LENGTH) errors.iccid = "tooLong";
  if (!String(values.carrierId ?? "").trim()) errors.carrierId = "required";
  if (!values.planType?.trim()) errors.planType = "required";
  if (!Number.isFinite(cost) || cost < 0) errors.monthlyCost = "invalid";

  return errors;
}

export const CARRIER_NAME_MIN_LENGTH = 2;
export const CARRIER_NAME_MAX_LENGTH = 40;

/**
 * Carrier name rules: required, 2 to 40 characters after trimming, and
 * unique ignoring case. Returns required, tooShort, tooLong or taken, or null.
 * @param {string | null | undefined} name
 * @param {{ id: number, name: string }[]} carriers
 * @param {number} [excludeId] the carrier being renamed
 */
export function carrierNameError(name, carriers, excludeId) {
  const value = name?.trim() ?? "";
  if (!value) return "required";
  if (value.length < CARRIER_NAME_MIN_LENGTH) return "tooShort";
  if (value.length > CARRIER_NAME_MAX_LENGTH) return "tooLong";

  const key = value.toLocaleLowerCase();
  const taken = carriers.some((carrier) => carrier.id !== excludeId && carrier.name.toLocaleLowerCase() === key);
  return taken ? "taken" : null;
}

/** Same shape check the API applies: no spaces, one "@", a dotted domain. */
export function isEmail(value) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}
