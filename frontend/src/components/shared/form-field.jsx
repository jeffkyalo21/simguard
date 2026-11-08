import { Label } from "@/components/ui/label";

/**
 * Label, control, optional hint and inline error. The render prop receives
 * the props that tie the control to its label, hint and error.
 * @param {{ id: string, label: string, hint?: string, error?: string,
 *   children: (props: { id: string, "aria-invalid"?: boolean,
 *     "aria-describedby"?: string }) => React.ReactNode }} props
 */
export function FormField({ id, label, hint, error, children }) {
  const errorId = `${id}-error`;
  const hintId = `${id}-hint`;
  const describedBy = [hint && hintId, error && errorId].filter(Boolean).join(" ");

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children({
        id,
        "aria-invalid": error ? true : undefined,
        "aria-describedby": describedBy || undefined,
      })}
      {hint && (
        <p id={hintId} className="text-sm text-muted-foreground">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="text-sm text-risk-flagged-foreground">
          {error}
        </p>
      )}
    </div>
  );
}
