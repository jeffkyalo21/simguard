import { NotFoundContent } from "@/components/shared/not-found-content";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.notFound.title };

// Inside the app the shell stays; only the content shows not found.
export default function AppNotFound() {
  return <NotFoundContent href="/dashboard" />;
}
