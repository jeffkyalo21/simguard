"use client";

import { flushSync } from "react-dom";
import { Monitor, Moon, Sun } from "lucide-react";
import { useTheme } from "next-themes";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { copy } from "@/lib/copy";

const options = [
  { value: "light", label: copy.theme.light, Icon: Sun },
  { value: "dark", label: copy.theme.dark, Icon: Moon },
  { value: "system", label: copy.theme.system, Icon: Monitor },
];

export function ThemeToggle() {
  const { theme, setTheme } = useTheme();

  // Cross-fades the whole page (opacity only) where View Transitions exist;
  // elsewhere, and with reduced motion, the theme switches instantly.
  function changeTheme(value) {
    const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    if (!document.startViewTransition || reduceMotion) {
      setTheme(value);
      return;
    }

    document.startViewTransition(() => flushSync(() => setTheme(value)));
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="touch-target size-10 rounded-full"
          aria-label={copy.theme.toggle}
        >
          {/* Both icons render; CSS picks one, so there is no hydration flash. */}
          <Sun className="dark:hidden" aria-hidden="true" />
          <Moon className="hidden dark:block" aria-hidden="true" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-36">
        <DropdownMenuRadioGroup value={theme} onValueChange={changeTheme}>
          {options.map(({ value, label, Icon }) => (
            <DropdownMenuRadioItem key={value} value={value}>
              <Icon aria-hidden="true" />
              {label}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
