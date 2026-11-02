import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";

/**
 * Button that shows a spinner and blocks repeat clicks while work runs.
 * @param {{ busy?: boolean } & React.ComponentProps<typeof Button>} props
 */
export function BusyButton({ busy = false, disabled, children, ...props }) {
  return (
    <Button disabled={busy || disabled} aria-busy={busy || undefined} {...props}>
      {busy && <Loader2 className="animate-spin" aria-hidden="true" />}
      {children}
    </Button>
  );
}
