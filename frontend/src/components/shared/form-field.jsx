import { Label } from "@/components/ui/label";

/**
 * Label, control and inline error. The render prop receives the props that
 * tie the control to its label and error.
 * @param {{ id: string, label: string, error?: string,
 *   children: (props: { id: string, "aria-invalid"?: boolean,
 *     "aria-describedby"?: string }) => React.ReactNode }} props
 */
export function FormField({ id, label, error, children }) {
  const errorId = `${id}-error`;

  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor={id}>{label}</Label>
      {children({
        id,
        "aria-invalid": error ? true : undefined,
        "aria-describedby": error ? errorId : undefined,
      })}
      {error && (
        <p id={errorId} className="text-sm text-risk-flagged-foreground">
          {error}
        </p>
      )}
    </div>
  );
}
