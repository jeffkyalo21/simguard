import { Smartphone } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.sims.title };

export default function SimsPage() {
  return <PlaceholderPage {...copy.pages.sims} icon={Smartphone} />;
}
