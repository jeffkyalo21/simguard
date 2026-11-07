import { Suspense } from "react";
import { AuthShell } from "@/components/auth/auth-shell";
import { FormFallback } from "@/components/auth/form-fallback";
import { MfaSetup } from "@/components/auth/mfa-setup";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.auth.setup.title };

export default function MfaSetupPage() {
  return (
    <AuthShell title={copy.auth.setup.title} intro={copy.auth.setup.intro} wide>
      <Suspense fallback={<FormFallback rows={3} />}>
        <MfaSetup />
      </Suspense>
    </AuthShell>
  );
}
