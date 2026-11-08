"use client";

import { createContext, use, useCallback, useMemo, useState } from "react";
import { authRequest, loginHref, signOutRequest } from "@/lib/auth/client";
import { ERROR_CODES } from "@/lib/auth/core";

const AuthContext = createContext(null);

/**
 * Holds the signed in user for client components. The (app) layout loads
 * the user on the server (backend /auth/me) and passes it in, so there is
 * no loading flash; `refresh` reads /api/auth/me again.
 * @param {{ user: { email: string, name?: string, role: string },
 *   children: React.ReactNode }} props
 */
export function AuthProvider({ user: initialUser, children }) {
  const [user, setUser] = useState(initialUser);

  const refresh = useCallback(async () => {
    const result = await authRequest("/api/auth/me", undefined, { method: "GET" });
    if (result.ok) {
      setUser(result.data);
      return result.data;
    }
    if (result.error.code === ERROR_CODES.unauthorized) {
      window.location.assign(loginHref({ reason: "expired" }));
    }
    return null;
  }, []);

  // A full page load drops every client cache (router, loaded data) with the
  // session, so the next person on this browser starts clean.
  const signOut = useCallback(async () => {
    await signOutRequest();
    window.location.assign(loginHref({ reason: "signedOut" }));
  }, []);

  const value = useMemo(
    () => ({ email: user.email, name: user.name, role: user.role, refresh, signOut }),
    [user, refresh, signOut]
  );

  return <AuthContext value={value}>{children}</AuthContext>;
}

/** @returns {{ email: string, name?: string, role: string,
 *   refresh: () => Promise<unknown>, signOut: () => Promise<void> }} */
export function useAuth() {
  const value = use(AuthContext);
  if (!value) throw new Error("useAuth must be used inside AuthProvider.");
  return value;
}
