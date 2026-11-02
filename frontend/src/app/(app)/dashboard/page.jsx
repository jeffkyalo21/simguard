import { DashboardView } from "@/components/dashboard/dashboard-view";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.dashboard.title };

export default function DashboardPage() {
  return <DashboardView />;
}
