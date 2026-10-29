import { Bell } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.alertDetail.title };

export default function AlertDetailPage() {
  return <PlaceholderPage {...copy.pages.alertDetail} icon={Bell} />;
}
