import { AdminView } from "@/components/admin/admin-view";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.admin.title };

// Where an Admin lands after signing in: scoring status and controls.
export default function AdminPage() {
  return <AdminView />;
}
