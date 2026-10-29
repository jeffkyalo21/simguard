"use client";

import Link from "next/link";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { copy } from "@/lib/copy";

// Placeholder form: submitting never sends credentials anywhere (auth is #15).
export function LoginForm() {
  function handleSubmit(event) {
    event.preventDefault();
    toast.info(copy.login.unavailable);
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="flex flex-col gap-2">
        <Label htmlFor="email">{copy.login.email}</Label>
        <Input id="email" name="email" type="email" autoComplete="email" required className="h-10" />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="password">{copy.login.password}</Label>
        <Input
          id="password"
          name="password"
          type="password"
          autoComplete="current-password"
          required
          className="h-10"
        />
      </div>
      <Button type="submit" className="mt-2 h-10 w-full">
        {copy.actions.signIn}
      </Button>
      <Link
        href="/login"
        className="self-center rounded-sm text-sm text-muted-foreground underline-offset-4 hover:underline"
      >
        {copy.login.forgotPassword}
      </Link>
    </form>
  );
}
