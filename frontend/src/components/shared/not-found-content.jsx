import Link from "next/link";
import { FileQuestion } from "lucide-react";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

/**
 * Shared body of the not-found pages.
 * @param {{ title?: string, text?: string, href?: string, action?: string }} props
 */
export function NotFoundContent({
  title = copy.notFound.title,
  text = copy.notFound.text,
  href = "/",
  action = copy.actions.goHome,
}) {
  return (
    <div className="flex flex-1 flex-col items-center justify-center gap-4 px-4 py-16 text-center">
      <FileQuestion className="size-10 text-muted-foreground" aria-hidden="true" />
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <p className="text-muted-foreground">{text}</p>
      <Button asChild className="h-10 px-4">
        <Link href={href}>{action}</Link>
      </Button>
    </div>
  );
}
