"use client";

import { useEffect, useRef } from "react";
import { AlertCircle } from "lucide-react";
import { copy } from "@/lib/copy";

/**
 * Error summary for the sign-in forms. It is a live region (role="alert")
 * and takes focus each time `focusKey` changes, so a screen reader reads it
 * and keyboard users start from it. Items with a `fieldId` link to their
 * field.
 * @param {{ items: { text: string, fieldId?: string }[], hint?: string,
 *   focusKey: number }} props
 */
export function ErrorSummary({ items, hint, focusKey }) {
  const ref = useRef(null);
  const visible = items.length > 0;

  useEffect(() => {
    if (visible) ref.current?.focus();
  }, [focusKey, visible]);

  if (!visible) return <div role="alert" aria-live="assertive" className="sr-only" />;

  return (
    <div
      ref={ref}
      tabIndex={-1}
      role="alert"
      aria-live="assertive"
      aria-labelledby="error-summary-title"
      className="flex gap-3 rounded-xl border border-risk-flagged-foreground/40 bg-risk-flagged p-4 text-sm text-risk-flagged-foreground"
    >
      <AlertCircle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
      <div className="flex flex-col gap-1.5">
        <h2 id="error-summary-title" className="font-semibold">
          {copy.auth.errorSummaryTitle}
        </h2>
        <ul className="flex flex-col gap-1">
          {items.map((item) => (
            <li key={`${item.fieldId ?? "form"}-${item.text}`}>
              {item.fieldId ? (
                <a
                  href={`#${item.fieldId}`}
                  onClick={(event) => {
                    event.preventDefault();
                    document.getElementById(item.fieldId)?.focus();
                  }}
                  className="underline underline-offset-2"
                >
                  {item.text}
                </a>
              ) : (
                item.text
              )}
            </li>
          ))}
        </ul>
        {hint && <p className="text-foreground/80">{hint}</p>}
      </div>
    </div>
  );
}
