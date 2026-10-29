import { LayoutDashboard } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.dashboard.title };

export default function DashboardPage() {
  return <PlaceholderPage {...copy.pages.dashboard} icon={LayoutDashboard} />;
}
