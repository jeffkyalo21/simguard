import { Bell } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.alerts.title };

export default function AlertsPage() {
  return <PlaceholderPage {...copy.pages.alerts} icon={Bell} />;
}
