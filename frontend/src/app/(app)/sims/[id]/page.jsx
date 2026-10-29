import { Smartphone } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.simDetail.title };

export default function SimDetailPage() {
  return <PlaceholderPage {...copy.pages.simDetail} icon={Smartphone} />;
}
