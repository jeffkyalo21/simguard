import { TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/**
 * What happened and what to do, plus a retry.
 * @param {{ message?: string, onRetry?: () => void, className?: string }} props
 */
export function ErrorState({ message = copy.errors.load, onRetry, className }) {
  return (
    <div role="alert" className={cn("flex flex-col items-center justify-center gap-3 py-10 text-center", className)}>
      <TriangleAlert className="size-8 text-muted-foreground" aria-hidden="true" />
      <p className="text-sm text-muted-foreground">{message}</p>
      {onRetry && (
        <Button variant="outline" onClick={onRetry} className="h-10 px-4">
          {copy.actions.tryAgain}
        </Button>
      )}
    </div>
  );
}
