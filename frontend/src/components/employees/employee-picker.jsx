"use client";

import { useEffect, useId, useState } from "react";
import { UserPlus, X } from "lucide-react";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { useResource } from "@/hooks/use-resource";
import { canSearch, employeeOption, employeeSummary } from "@/lib/employee-picker";
import { listEmployees } from "@/lib/data";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.allocations.form;
const SEARCH_DELAY_MS = 250;
const RESULT_LIMIT = 8;

/** The read-only confirmation of who was picked. */
function EmployeeSummary({ employee, onClear }) {
  return (
    <section aria-label={text.summaryTitle} className="rounded-xl border border-glass-border bg-background/60 p-3 text-sm">
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
        {employeeSummary(employee).map((row) => (
          <div key={row.key} className="contents">
            <dt className="text-muted-foreground">{text.summaryLabels[row.key]}</dt>
            <dd className="min-w-0 break-words font-medium">{row.value || copy.common.noValue}</dd>
          </div>
        ))}
      </dl>
      <Button type="button" variant="ghost" className="mt-2 h-9 px-2" onClick={onClear}>
        <X aria-hidden="true" />
        {text.changeEmployee}
      </Button>
    </section>
  );
}

/**
 * One searchable employee field (name or email) with a read-only summary of
 * the pick. `onAdd` is given only to roles that may add employees; it adds an
 * "Add new employee" row under the results (also when there are none).
 * @param {{ id: string, label: string, value: object | null, error?: string,
 *   onChange: (employee: object | null) => void,
 *   onAdd?: (query: string) => void }} props
 */
export function EmployeePicker({ id, label, value, error, onChange, onAdd }) {
  const listId = useId();
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);

  useEffect(() => {
    const timer = setTimeout(() => setQuery(search.trim()), SEARCH_DELAY_MS);
    return () => clearTimeout(timer);
  }, [search]);

  const searching = canSearch(query);
  const results = useResource(
    () => (searching ? listEmployees({ search: query, pageSize: RESULT_LIMIT }) : Promise.resolve(null)),
    [query]
  );
  const items = results.data?.items ?? [];
  const waiting = canSearch(search) && (query !== search.trim() || (searching && !results.data && !results.error));
  const showResults = open && !value && canSearch(search);
  // The "Add new employee" row is the last option.
  const optionCount = items.length + (onAdd ? 1 : 0);

  function pick(employee) {
    onChange(employee);
    setSearch("");
    setQuery("");
    setOpen(false);
    setActive(-1);
  }

  function handleKeyDown(event) {
    if (!showResults) return;
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      const step = event.key === "ArrowDown" ? 1 : -1;
      setActive((current) => (optionCount ? (current + step + optionCount) % optionCount : -1));
    } else if (event.key === "Enter" && active >= 0) {
      // Pick the highlighted row instead of submitting the form.
      event.preventDefault();
      if (active < items.length) pick(items[active]);
      else onAdd?.(search.trim());
    } else if (event.key === "Escape") {
      // Close the list first; a second Escape closes the dialog.
      event.preventDefault();
      event.stopPropagation();
      setOpen(false);
      setActive(-1);
    }
  }

  const optionId = (index) => `${listId}-option-${index}`;
  const status = !showResults || waiting
    ? waiting && open ? text.employeeSearching : ""
    : results.error ? text.loadError : items.length ? text.resultsCount(items.length) : text.noEmployeeMatch;

  return (
    <div className="flex flex-col gap-3">
      <FormField id={id} label={label} hint={value ? undefined : text.employeeHint} error={error}>
        {(field) => (
          <Input
            {...field}
            type="text"
            role="combobox"
            aria-autocomplete="list"
            aria-expanded={showResults}
            aria-controls={listId}
            aria-activedescendant={showResults && active >= 0 ? optionId(active) : undefined}
            value={value ? value.name : search}
            readOnly={Boolean(value)}
            onChange={(event) => {
              setSearch(event.target.value);
              setOpen(true);
              setActive(-1);
            }}
            onFocus={() => setOpen(true)}
            onKeyDown={handleKeyDown}
            placeholder={text.employeeSearchPlaceholder}
            autoComplete="off"
            className="h-10"
          />
        )}
      </FormField>

      <p role="status" className="sr-only">
        {status}
      </p>

      {showResults && (
        <ul
          id={listId}
          role="listbox"
          aria-label={label}
          className="max-h-56 overflow-y-auto rounded-xl border border-glass-border bg-background/80 p-1 text-sm"
        >
          {items.map((employee, index) => {
            const option = employeeOption(employee);
            return (
              <li
                key={option.id}
                id={optionId(index)}
                role="option"
                aria-selected={active === index}
                // mousedown keeps the focus in the input, so the click is not lost to a blur.
                onMouseDown={(event) => event.preventDefault()}
                onClick={() => pick(employee)}
                className={cn("cursor-pointer rounded-lg px-3 py-2", active === index ? "bg-accent text-accent-foreground" : "hover:bg-accent/60")}
              >
                <span className="block font-medium">{option.name}</span>
                {option.detail && <span className="block text-muted-foreground">{option.detail}</span>}
              </li>
            );
          })}
          {!waiting && !items.length && (
            <li role="presentation" className="px-3 py-2 text-muted-foreground">
              {results.error ? text.loadError : text.noEmployeeMatch}
            </li>
          )}
          {onAdd && (
            <li
              id={optionId(items.length)}
              role="option"
              aria-selected={active === items.length}
              onMouseDown={(event) => event.preventDefault()}
              onClick={() => onAdd(search.trim())}
              className={cn(
                "flex cursor-pointer items-center gap-2 rounded-lg px-3 py-2 font-medium",
                active === items.length ? "bg-accent text-accent-foreground" : "hover:bg-accent/60"
              )}
            >
              <UserPlus className="size-4" aria-hidden="true" />
              {text.addEmployee}
            </li>
          )}
        </ul>
      )}

      {value && <EmployeeSummary employee={value} onClear={() => onChange(null)} />}
    </div>
  );
}
