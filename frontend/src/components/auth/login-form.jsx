"use client";

import { useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { toast } from "sonner";
import { BusyButton } from "@/components/shared/busy-button";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { isEmail } from "@/lib/validation";
import { copy } from "@/lib/copy";

const SIGN_IN_DELAY_MS = 600;

function validate({ email, password }) {
  const errors = {};
  if (!email.trim()) errors.email = copy.login.errors.emailRequired;
  else if (!isEmail(email.trim())) errors.email = copy.login.errors.emailInvalid;
  if (!password) errors.password = copy.login.errors.passwordRequired;
  return errors;
}

// Mock sign-in: any valid-looking credentials go to the dashboard.
export function LoginForm() {
  const router = useRouter();
  const [errors, setErrors] = useState({});
  const [busy, setBusy] = useState(false);
  const emailRef = useRef(null);
  const passwordRef = useRef(null);

  async function handleSubmit(event) {
    event.preventDefault();
    const form = new FormData(event.currentTarget);
    const values = { email: String(form.get("email")), password: String(form.get("password")) };
    const nextErrors = validate(values);

    setErrors(nextErrors);
    if (nextErrors.email) return emailRef.current?.focus();
    if (nextErrors.password) return passwordRef.current?.focus();

    setBusy(true);
    // TODO #15 replace with real auth
    await new Promise((resolve) => setTimeout(resolve, SIGN_IN_DELAY_MS));
    setBusy(false);
    router.push("/dashboard");
  }

  return (
    <form onSubmit={handleSubmit} noValidate className="flex flex-col gap-4" aria-busy={busy || undefined}>
      <FormField id="email" label={copy.login.email} error={errors.email}>
        {(field) => (
          <Input
            {...field}
            ref={emailRef}
            name="email"
            type="email"
            autoComplete="email"
            placeholder={copy.login.emailPlaceholder}
            className="h-10"
          />
        )}
      </FormField>
      <FormField id="password" label={copy.login.password} error={errors.password}>
        {(field) => (
          <Input
            {...field}
            ref={passwordRef}
            name="password"
            type="password"
            autoComplete="current-password"
            placeholder={copy.login.passwordPlaceholder}
            className="h-10"
          />
        )}
      </FormField>
      <BusyButton type="submit" busy={busy} size="lg" className="mt-2 h-10 w-full">
        {copy.actions.signIn}
      </BusyButton>
      <Button
        type="button"
        variant="ghost"
        onClick={() => toast.info(copy.login.forgotHint)}
        className="h-10 self-center text-muted-foreground"
      >
        {copy.login.forgotPassword}
      </Button>
    </form>
  );
}
