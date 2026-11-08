"use client";

import { useResource } from "@/hooks/use-resource";
import { listCarriers, listDepartments, listTiers } from "@/lib/data";

// Reference lists that change at run time, loaded from the API for filters,
// forms and labels. Each returns [] while loading or after an error, so a
// screen still renders; the screen's own data shows the error.

/** GET /departments: [{ id, name, employeeCount }]. */
export function useDepartments() {
  const { data } = useResource(listDepartments, ["departments"]);
  return data?.items ?? [];
}

/** GET /tiers: [{ tier, dataMb, voiceMinutes, sms, monthlyCost }]. */
export function useTiers() {
  const { data } = useResource(listTiers, ["tiers"]);
  return data ?? [];
}

/** GET /carriers: [{ id, name, createdAt, simCount }]. */
export function useCarriers() {
  const { data } = useResource(listCarriers, ["carriers"]);
  return data ?? [];
}
