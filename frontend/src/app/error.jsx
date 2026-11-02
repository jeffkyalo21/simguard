"use client";

import { TriangleAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

// Error boundaries must be Client Components. No technical details are shown.
export default function ErrorPage({ retry }) {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-dvh flex-col items-center justify-center gap-4 px-4 text-center">
      <TriangleAlert className="size-10 text-muted-foreground" aria-hidden="true" />
      <div role="alert">
        <h1 className="text-lg font-medium text-muted-foreground">{copy.errors.generic}</h1>
      </div>
      <Button onClick={() => retry()} className="h-10 px-4">
        {copy.actions.tryAgain}
      </Button>
    </main>
  );
}
