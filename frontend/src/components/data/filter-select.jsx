"use client";

import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const ALL = "all";

/**
 * Toolbar dropdown. Shows its label until a value is picked; "All" clears.
 * @param {{ label: string, value: string, options: { value: string, label: string }[],
 *   onChange: (value: string) => void, includeAll?: boolean, className?: string }} props
 */
export function FilterSelect({ label, value, options, onChange, includeAll = true, className }) {
  const selected = options.find((option) => option.value === value);

  return (
    <Select
      value={value || ALL}
      onValueChange={(next) => onChange(next === ALL ? "" : next)}
    >
      <SelectTrigger
        aria-label={label}
        className={cn("touch-target h-10 min-w-28 bg-glass-control data-[size=default]:h-10", className)}
      >
        <SelectValue>{selected ? selected.label : label}</SelectValue>
      </SelectTrigger>
      <SelectContent position="popper" align="start">
        {includeAll && <SelectItem value={ALL}>{copy.filters.all}</SelectItem>}
        {options.map((option) => (
          <SelectItem key={option.value} value={option.value}>
            {option.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
