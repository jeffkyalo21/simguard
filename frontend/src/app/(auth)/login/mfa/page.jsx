import { Suspense } from "react";
import { AuthShell } from "@/components/auth/auth-shell";
import { FormFallback } from "@/components/auth/form-fallback";
import { MfaForm } from "@/components/auth/mfa-form";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.auth.mfa.title };

export default function MfaPage() {
  return (
    <AuthShell title={copy.auth.mfa.title}>
      <Suspense fallback={<FormFallback rows={1} />}>
        <MfaForm />
      </Suspense>
    </AuthShell>
  );
}
