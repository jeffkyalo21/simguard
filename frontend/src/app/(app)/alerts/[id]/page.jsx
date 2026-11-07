import { notFound } from "next/navigation";
import { AlertDetail } from "@/components/alerts/alert-detail";
import { callBackend, getSessionToken } from "@/lib/auth/server";
import { isPositiveId } from "@/lib/ids";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.alertDetail.title };

// Like sims/[id]: the route reads its params and blocks rather than streaming
// a shell, because no parent segment has a Suspense boundary (that keeps the
// alert 404 a real 404) and the nav needs the pathname.
export const instant = false;

export default async function AlertDetailPage({ params }) {
  const { id } = await params;
  if (!isPositiveId(id)) notFound();

  // Same as sims/[id]: only a 404 from the API is a 404 page.
  const result = await callBackend(`/alerts/${id}`, { token: await getSessionToken() });
  if (result.status === 404) notFound();

  return <AlertDetail id={Number(id)} />;
}
