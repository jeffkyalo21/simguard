import { copy } from "@/lib/copy";

// The API sends UTC; the UI always shows Nairobi time, never the browser's zone.
const TIME_ZONE = "Africa/Nairobi";
const LOCALE = "en-KE";

const dateTimeFormat = new Intl.DateTimeFormat(LOCALE, {
  timeZone: TIME_ZONE,
  dateStyle: "medium",
  timeStyle: "short",
});

const dateFormat = new Intl.DateTimeFormat(LOCALE, {
  timeZone: TIME_ZONE,
  dateStyle: "medium",
});

function parseUtc(iso) {
  if (!iso) return null;

  // Treat timestamps without an offset as UTC rather than local time.
  const value = /([zZ]|[+-]\d{2}:?\d{2})$/.test(iso) ? iso : `${iso}Z`;
  const date = new Date(value);

  return Number.isNaN(date.getTime()) ? null : date;
}

/** @param {string | null | undefined} iso UTC ISO timestamp */
export function formatDateTime(iso) {
  const date = parseUtc(iso);
  return date ? dateTimeFormat.format(date) : copy.common.noValue;
}

/** @param {string | null | undefined} iso UTC ISO timestamp */
export function formatDate(iso) {
  const date = parseUtc(iso);
  return date ? dateFormat.format(date) : copy.common.noValue;
}

/** KES with no decimals for whole amounts, two otherwise. */
export function formatKes(amount) {
  const value = Number(amount);

  if (amount === null || amount === undefined || Number.isNaN(value)) {
    return copy.common.noValue;
  }

  const whole = Number.isInteger(value);

  return new Intl.NumberFormat(LOCALE, {
    style: "currency",
    currency: "KES",
    minimumFractionDigits: whole ? 0 : 2,
    maximumFractionDigits: 2,
  }).format(value);
}

/** "+254712345678" becomes "+254 712 345 678". Other numbers are unchanged. */
export function formatPhone(phone) {
  if (!phone) return copy.common.noValue;

  const match = /^\+254(\d{3})(\d{3})(\d{3})$/.exec(phone);
  return match ? `+254 ${match[1]} ${match[2]} ${match[3]}` : phone;
}

const BYTE_UNITS = ["B", "KB", "MB", "GB", "TB"];

/**
 * Data volume using decimal units (1 MB = 1,000,000 bytes), as carriers bill.
 * @param {number} bytes
 */
export function formatBytes(bytes) {
  const value = Number(bytes);

  if (bytes === null || bytes === undefined || Number.isNaN(value)) {
    return copy.common.noValue;
  }

  let size = Math.abs(value);
  let unit = 0;

  while (size >= 1000 && unit < BYTE_UNITS.length - 1) {
    size /= 1000;
    unit += 1;
  }

  const digits = unit === 0 || size >= 100 ? 0 : 1;
  const formatted = new Intl.NumberFormat(LOCALE, {
    maximumFractionDigits: digits,
  }).format(Math.sign(value) * size);

  return `${formatted} ${BYTE_UNITS[unit]}`;
}
