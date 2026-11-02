import { BarChart3 } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.usage.title };

export default function UsagePage() {
  return <PlaceholderPage {...copy.pages.usage} icon={BarChart3} />;
}
