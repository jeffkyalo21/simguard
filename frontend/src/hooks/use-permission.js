"use client";

import { useAuth } from "@/components/auth/auth-provider";
import { deniedReason } from "@/lib/permissions";

/**
 * Whether the signed-in role may perform an action from lib/permissions.js,
 * and the line DisabledReason shows when it may not. The backend refuses
 * the same actions with 403; this only keeps the buttons honest.
 * @param {string} action for example "sims.edit"
 * @returns {{ allowed: boolean, reason: string | null }}
 */
export function usePermission(action) {
  const { role } = useAuth();
  const reason = deniedReason(role, action);
  return { allowed: reason === null, reason };
}
