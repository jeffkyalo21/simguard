import { Search } from "lucide-react";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/**
 * Page search field. It is what "/" focuses on pages that have one.
 * @param {{ label: string, placeholder?: string, value: string,
 *   onChange: (value: string) => void, className?: string }} props
 */
export function SearchInput({ label, placeholder, value, onChange, className }) {
  return (
    <label
      className={cn(
        "relative flex h-10 items-center rounded-lg border border-input bg-glass-control transition-shadow duration-150 focus-within:ring-3 focus-within:ring-ring/50",
        className
      )}
    >
      <span className="sr-only">{label}</span>
      <Search className="pointer-events-none absolute left-3 size-4 text-muted-foreground" aria-hidden="true" />
      <input
        type="search"
        data-page-search
        value={value}
        onChange={(event) => onChange(event.target.value)}
        placeholder={placeholder}
        aria-keyshortcuts={copy.topbar.searchShortcut}
        className="h-full w-full min-w-0 rounded-lg bg-transparent pr-3 pl-9 text-sm text-foreground outline-none placeholder:text-muted-foreground focus-visible:outline-none"
      />
    </label>
  );
}
