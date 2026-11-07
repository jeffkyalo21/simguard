import { ShieldCheck } from "lucide-react";
import { SignOutButton } from "@/components/auth/sign-out-button";
import { GlassCard } from "@/components/glass/glass-card";
import { PageHeader } from "@/components/layout/page-header";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.admin.title };

// Placeholder: where an Admin lands after signing in.
export default function AdminPage() {
  return (
    <>
      <PageHeader title={copy.admin.title} />
      <GlassCard variant="strong" className="flex flex-col items-center gap-4 py-12 text-center">
        <ShieldCheck className="size-10 text-muted-foreground" aria-hidden="true" />
        <p className="text-lg font-medium">{copy.admin.comingSoon}</p>
        <p className="text-sm text-muted-foreground">{copy.admin.text}</p>
        <SignOutButton variant="outline" className="h-10 px-4" />
      </GlassCard>
    </>
  );
}
