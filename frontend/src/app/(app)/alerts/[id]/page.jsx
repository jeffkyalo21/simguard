import { Bell } from "lucide-react";
import { PlaceholderPage } from "@/components/shared/placeholder-page";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.alertDetail.title };

// Like sims/[id]: the route reads its params and blocks rather than streaming
// a shell, because no parent segment has a Suspense boundary (that keeps the
// SIM 404 a real 404) and the nav needs the pathname.
export const instant = false;

export default async function AlertDetailPage({ params }) {
  await params;
  return <PlaceholderPage {...copy.pages.alertDetail} icon={Bell} />;
}
