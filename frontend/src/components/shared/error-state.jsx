import { TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { isFinalLoadError, loadErrorMessage } from "@/lib/api";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/**
 * What happened and what to do, plus a retry. With `error`, the message
 * follows the failure (403, expired session, network) and the retry is
 * hidden when trying again cannot help.
 * @param {{ error?: unknown, message?: string, onRetry?: () => void, className?: string }} props
 */
export function ErrorState({ error, message, onRetry, className }) {
  const text = message ?? (error ? loadErrorMessage(error) : copy.errors.load);
  const retry = error && isFinalLoadError(error) ? undefined : onRetry;

  return (
    <div role="alert" className={cn("flex flex-col items-center justify-center gap-3 py-10 text-center", className)}>
      <TriangleAlert className="size-8 text-muted-foreground" aria-hidden="true" />
      <p className="text-sm text-muted-foreground">{text}</p>
      {retry && (
        <Button variant="outline" onClick={retry} className="h-10 px-4">
          {copy.actions.tryAgain}
        </Button>
      )}
    </div>
  );
}
