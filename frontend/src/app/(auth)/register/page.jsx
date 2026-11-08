import Link from "next/link";
import { AuthShell } from "@/components/auth/auth-shell";
import { RegisterForm } from "@/components/landing/register-form";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.landing.register.title };

// The no-JavaScript target of every Register control; with JavaScript they
// open the same form in a dialog.
export default function RegisterPage() {
  return (
    <AuthShell title={copy.landing.register.title} intro={copy.landing.register.intro} wide>
      <RegisterForm idPrefix="page" />
      <p className="text-sm text-muted-foreground">
        {copy.landing.register.haveAccount}{" "}
        <Link href="/login" className="rounded-sm font-medium text-foreground underline underline-offset-4">
          {copy.actions.signIn}
        </Link>
      </p>
    </AuthShell>
  );
}
