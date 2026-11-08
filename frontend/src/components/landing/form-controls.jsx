"use client";

import { ChevronDown } from "lucide-react";
import { cn } from "@/lib/utils";

// Same look as Input, for the native select and the textarea.
export const controlClass =
  "w-full min-w-0 rounded-lg border border-input bg-transparent px-3 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40";

/**
 * Native select with a placeholder option, so it works without JavaScript.
 * The option list is drawn by the browser: color-scheme makes it follow the
 * theme, and the options get the popover colours so their text never
 * inherits light ink onto a white list.
 */
export function SelectField({ field, name, placeholder, options }) {
  return (
    <div className="relative">
      <select
        {...field}
        name={name}
        defaultValue=""
        className={cn(
          controlClass,
          "h-11 appearance-none pr-10 [color-scheme:light] dark:[color-scheme:dark] [&>option]:bg-popover [&>option]:text-popover-foreground"
        )}
      >
        <option value="">{placeholder}</option>
        {options.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <ChevronDown
        className="pointer-events-none absolute top-1/2 right-3.5 size-4 -translate-y-1/2 text-muted-foreground"
        aria-hidden="true"
      />
    </div>
  );
}
