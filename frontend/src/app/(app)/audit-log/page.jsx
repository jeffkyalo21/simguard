import { ScrollText } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.auditLog.title };

export default function AuditLogPage() {
  return <PlaceholderPage {...copy.pages.auditLog} icon={ScrollText} />;
}
