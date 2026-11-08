import { Suspense } from "react";
import { AuthShell } from "@/components/auth/auth-shell";
import { FormFallback } from "@/components/auth/form-fallback";
import { LoginForm } from "@/components/auth/login-form";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.login.title };

// Step 1 of sign-in. The form reads ?next and ?reason, so it renders on
// the client behind a boundary.
export default function LoginPage() {
  return (
    <AuthShell title={copy.login.title}>
      <Suspense fallback={<FormFallback />}>
        <LoginForm />
      </Suspense>
    </AuthShell>
  );
}
