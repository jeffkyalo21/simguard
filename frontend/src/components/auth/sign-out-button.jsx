"use client";

import { useState } from "react";
import { LogOut } from "lucide-react";
import { useAuth } from "@/components/auth/auth-provider";
import { BusyButton } from "@/components/shared/busy-button";
import { copy } from "@/lib/copy";

/** Calls the logout route handler, then loads /login. */
export function SignOutButton(props) {
  const { signOut } = useAuth();
  const [busy, setBusy] = useState(false);

  return (
    <BusyButton
      type="button"
      busy={busy}
      onClick={() => {
        setBusy(true);
        signOut();
      }}
      {...props}
    >
      {!busy && <LogOut aria-hidden="true" />}
      {copy.actions.signOut}
    </BusyButton>
  );
}
