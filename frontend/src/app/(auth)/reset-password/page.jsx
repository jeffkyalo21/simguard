import { AuthShell } from "@/components/auth/auth-shell";
import { ResetPasswordForm } from "@/components/auth/reset-password-form";
import { copy } from "@/lib/copy";

// The link carries its token in the fragment. The page asks browsers not to
// send a Referer (also set as a response header in next.config.mjs) and is
// kept out of search results.
export const metadata = {
  title: copy.login.reset.title,
  referrer: "no-referrer",
  robots: { index: false, follow: false },
};

export default function ResetPasswordPage() {
  return (
    <AuthShell title={copy.login.reset.title}>
      <ResetPasswordForm />
    </AuthShell>
  );
}
