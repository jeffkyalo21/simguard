/** True for a positive whole number, the only ids the API can know. */
export function isPositiveId(id) {
  return /^\d{1,9}$/.test(String(id)) && Number(id) > 0;
}
