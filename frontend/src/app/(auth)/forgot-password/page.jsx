import { AuthShell } from "@/components/auth/auth-shell";
import { ForgotPasswordForm } from "@/components/auth/forgot-password-form";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.login.forgot.title };

export default function ForgotPasswordPage() {
  return (
    <AuthShell title={copy.login.forgot.title} intro={copy.login.forgot.intro}>
      <ForgotPasswordForm />
    </AuthShell>
  );
}
