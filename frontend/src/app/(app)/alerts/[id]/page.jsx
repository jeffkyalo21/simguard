import { notFound } from "next/navigation";
import { AlertDetail } from "@/components/alerts/alert-detail";
import { isPossibleAlertId } from "@/lib/data";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.alertDetail.title };

// Like sims/[id]: the route reads its params and blocks rather than streaming
// a shell, because no parent segment has a Suspense boundary (that keeps the
// SIM 404 a real 404) and the nav needs the pathname.
export const instant = false;

export default async function AlertDetailPage({ params }) {
  const { id } = await params;
  if (!isPossibleAlertId(id)) notFound();
  return <AlertDetail id={Number(id)} />;
}
