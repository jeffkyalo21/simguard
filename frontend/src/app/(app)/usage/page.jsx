import { UsageView } from "@/components/usage/usage-view";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.usage.title };

export default function UsagePage() {
  return <UsageView />;
}
