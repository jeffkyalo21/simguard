import Link from "next/link";
import { LoginForm } from "@/components/auth/login-form";
import { GlassCard } from "@/components/glass/glass-card";
import { Logo } from "@/components/layout/logo";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.login.title };

// Strong glass card on the static backdrop. No real auth until #15.
export default function LoginPage() {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-dvh items-center justify-center px-4 py-12">
      <GlassCard
        variant="strong"
        className="flex w-full max-w-[400px] flex-col gap-6 [--card-pad:32px] [--radius-card:32px]"
      >
        <Link href="/" className="w-fit rounded-md">
          <Logo />
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{copy.login.title}</h1>
        <LoginForm />
      </GlassCard>
    </main>
  );
}
