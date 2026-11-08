"use client";

import { useEffect, useRef, useState } from "react";
import { ChevronDown, CircleCheck } from "lucide-react";
import { FormField } from "@/components/shared/form-field";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { isEmail } from "@/lib/validation";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

const text = copy.landing.leadForm;

// Same look as Input, for the native select and the textarea.
export const controlClass =
  "w-full min-w-0 rounded-lg border border-input bg-transparent px-3 text-base transition-colors outline-none focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 md:text-sm dark:bg-input/30 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40";

// Field order, used to focus the first error.
const ORDER = ["name", "email", "company", "sims"];

function read(form) {
  const data = new FormData(form);
  return Object.fromEntries(ORDER.map((key) => [key, String(data.get(key) ?? "")]));
}

function validate(values) {
  const errors = {};
  if (!values.name.trim()) errors.name = text.errors.name;
  if (!values.email.trim()) errors.email = text.errors.emailRequired;
  else if (!isEmail(values.email.trim())) errors.email = text.errors.emailInvalid;
  if (!values.company.trim()) errors.company = text.errors.company;
  if (!values.sims) errors.sims = text.errors.sims;
  return errors;
}

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

/**
 * The contact section form. It checks fields in the browser, shows inline
 * errors and focuses the first one. Nothing is sent or stored: a valid
 * submit only shows the thank-you state. (Requests for an account go through
 * RegisterForm.)
 * @param {{ kind?: string, submitLabel: string, wide?: boolean }} props
 */
export function LeadForm({ kind = "contact", submitLabel, wide = false }) {
  const [errors, setErrors] = useState({});
  const [tried, setTried] = useState(false);
  const [done, setDone] = useState(false);
  const formRef = useRef(null);
  const successRef = useRef(null);
  const focusFirstField = useRef(false);
  const id = (key) => `${kind}-${key}`;

  useEffect(() => {
    if (done) successRef.current?.focus();
    else if (focusFirstField.current) {
      focusFirstField.current = false;
      formRef.current?.elements.namedItem("name")?.focus();
    }
  }, [done]);

  function handleSubmit(event) {
    event.preventDefault();
    const form = event.currentTarget;
    const nextErrors = validate(read(form));

    setTried(true);
    setErrors(nextErrors);
    const first = ORDER.find((key) => nextErrors[key]);
    if (first) {
      form.elements.namedItem(first)?.focus();
      return;
    }
    setDone(true);
  }

  function reset() {
    focusFirstField.current = true;
    setErrors({});
    setTried(false);
    setDone(false);
  }

  if (done) {
    return (
      <div ref={successRef} tabIndex={-1} className="lead-success flex flex-col items-start gap-4 outline-none">
        <CircleCheck className="size-8 text-foreground" aria-hidden="true" />
        <p className="text-lg font-medium">{text.success}</p>
        <Button type="button" variant="outline" onClick={reset} className="h-11 px-5">
          {copy.landing.contact.again}
        </Button>
        <p className="text-xs text-muted-foreground">{text.note}</p>
      </div>
    );
  }

  return (
    <form
      ref={formRef}
      onSubmit={handleSubmit}
      onChange={tried ? (event) => setErrors(validate(read(event.currentTarget))) : undefined}
      noValidate
      className="flex flex-col gap-5"
    >
      <div className={cn("grid gap-4", wide && "sm:grid-cols-2")}>
        <FormField id={id("name")} label={text.name} error={errors.name}>
          {(field) => <Input {...field} name="name" autoComplete="name" className="h-11 px-3" />}
        </FormField>
        <FormField id={id("email")} label={text.email} error={errors.email}>
          {(field) => (
            <Input
              {...field}
              name="email"
              type="email"
              autoComplete="email"
              placeholder={text.emailPlaceholder}
              className="h-11 px-3"
            />
          )}
        </FormField>
        <FormField id={id("company")} label={text.company} error={errors.company}>
          {(field) => <Input {...field} name="company" autoComplete="organization" className="h-11 px-3" />}
        </FormField>
        <FormField id={id("sims")} label={text.sims} error={errors.sims}>
          {(field) => (
            <SelectField field={field} name="sims" placeholder={text.simsPlaceholder} options={text.simsOptions} />
          )}
        </FormField>
        <div className={cn(wide && "sm:col-span-2")}>
          <FormField id={id("message")} label={text.message}>
            {(field) => <textarea {...field} name="message" rows={4} className={cn(controlClass, "resize-y py-2.5")} />}
          </FormField>
        </div>
      </div>
      <div className="flex flex-col items-start gap-2">
        <Button type="submit" size="lg" className="h-11 px-6">
          {submitLabel}
        </Button>
        <p className="text-xs text-muted-foreground">{text.note}</p>
      </div>
    </form>
  );
}
