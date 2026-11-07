"use client";

import { useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import { Input } from "@/components/ui/input";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/**
 * Password field with a show and hide toggle. The toggle is a real button
 * (Tab reaches it, Enter or Space presses it) with a fixed name and
 * aria-pressed, so screen readers announce "Show password, pressed".
 * @param {React.ComponentProps<"input"> & { ref?: React.Ref<HTMLInputElement> }} props
 */
export function PasswordInput({ id, className, ref, ...props }) {
  const [visible, setVisible] = useState(false);
  const text = copy.auth;

  return (
    <div className="relative">
      <Input
        {...props}
        ref={ref}
        id={id}
        type={visible ? "text" : "password"}
        autoCapitalize="none"
        autoCorrect="off"
        spellCheck={false}
        className={cn("h-10 pr-12", className)}
      />
      <button
        type="button"
        aria-controls={id}
        aria-pressed={visible}
        aria-label={text.showPassword}
        title={visible ? text.hidePassword : text.showPassword}
        onClick={() => setVisible((value) => !value)}
        className="absolute inset-y-0 right-0 flex w-11 items-center justify-center rounded-r-lg text-muted-foreground hover:text-foreground"
      >
        {visible ? <EyeOff className="size-4" aria-hidden="true" /> : <Eye className="size-4" aria-hidden="true" />}
      </button>
    </div>
  );
}
