import Link from "next/link";
import { GlassCard } from "@/components/glass/glass-card";
import { Logo } from "@/components/layout/logo";

/**
 * Shared frame of the sign-in screens: the strong glass card on the static
 * backdrop, the logo, a heading and an optional intro line.
 * @param {{ title: string, intro?: string, wide?: boolean, children: React.ReactNode }} props
 */
export function AuthShell({ title, intro, wide = false, children }) {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-dvh items-center justify-center px-4 py-12">
      <GlassCard
        variant="strong"
        className={`flex w-full flex-col gap-6 [--card-pad:32px] [--radius-card:32px] ${wide ? "max-w-[480px]" : "max-w-[400px]"}`}
      >
        <Link href="/" className="w-fit rounded-md">
          <Logo />
        </Link>
        <div className="flex flex-col gap-2">
          <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
          {intro && <p className="text-sm text-muted-foreground">{intro}</p>}
        </div>
        {children}
      </GlassCard>
    </main>
  );
}
