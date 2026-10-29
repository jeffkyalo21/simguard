import Link from "next/link";
import { FileQuestion } from "lucide-react";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.notFound.title };

export default function NotFound() {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-dvh flex-col items-center justify-center gap-4 bg-background px-4 text-center">
      <FileQuestion className="size-10 text-muted-foreground" aria-hidden="true" />
      <h1 className="text-2xl font-semibold tracking-tight">{copy.notFound.title}</h1>
      <p className="text-muted-foreground">{copy.notFound.text}</p>
      <Button asChild className="h-10 px-4">
        <Link href="/">{copy.actions.goHome}</Link>
      </Button>
    </main>
  );
}
