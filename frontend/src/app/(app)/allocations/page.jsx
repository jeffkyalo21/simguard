import { ArrowLeftRight } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.allocations.title };

export default function AllocationsPage() {
  return <PlaceholderPage {...copy.pages.allocations} icon={ArrowLeftRight} />;
}
