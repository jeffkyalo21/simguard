import { Suspense } from "react";
import { AuthShell } from "@/components/auth/auth-shell";
import { ChangePasswordForm } from "@/components/auth/change-password-form";
import { FormFallback } from "@/components/auth/form-fallback";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.auth.changePassword.title };

export default function ChangePasswordPage() {
  return (
    <AuthShell title={copy.auth.changePassword.title} intro={copy.auth.changePassword.intro}>
      <Suspense fallback={<FormFallback rows={3} />}>
        <ChangePasswordForm />
      </Suspense>
    </AuthShell>
  );
}
