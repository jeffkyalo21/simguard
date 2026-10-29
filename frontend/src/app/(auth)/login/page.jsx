import Link from "next/link";
import { Logo } from "@/components/layout/logo";
import { LoginForm } from "@/components/auth/login-form";
import { Card, CardContent, CardHeader } from "@/components/ui/card";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.login.title };

// Plain style. No real auth until #15.
export default function LoginPage() {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-dvh items-center justify-center bg-muted px-4 py-12">
      <Card className="w-full max-w-[400px] gap-6 py-8">
        <CardHeader className="items-center gap-4 px-8 text-center">
          <Link href="/" className="justify-self-center rounded-md">
            <Logo />
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">{copy.login.title}</h1>
        </CardHeader>
        <CardContent className="px-8">
          <LoginForm />
        </CardContent>
      </Card>
    </main>
  );
}
