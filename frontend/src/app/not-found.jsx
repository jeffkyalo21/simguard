import { NotFoundContent } from "@/components/shared/not-found-content";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.notFound.title };

export default function NotFound() {
  return (
    <main id="main" tabIndex={-1} className="flex min-h-dvh flex-col">
      <NotFoundContent />
    </main>
  );
}
