import { notFound } from "next/navigation";
import { SimProfile } from "@/components/sims/sim-profile";
import { callBackend, getSessionToken } from "@/lib/auth/server";
import { isPositiveId } from "@/lib/ids";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.simDetail.title };

// The page reads its id before rendering, so it may block navigation rather
// than show an instant shell. notFound() runs before anything streams, so an
// unknown id gets a real 404. Keep Suspense boundaries (and loading.jsx)
// out of this route's parents, or the status is fixed at 200.
export const instant = false;

export default async function SimDetailPage({ params }) {
  const { id } = await params;
  if (!isPositiveId(id)) notFound();

  // Ask the API whether the SIM exists, so an unknown id is a real 404. Any
  // other failure (403 for Admin, expiry, network) is left to the screen,
  // which shows the matching message.
  const result = await callBackend(`/sims/${id}`, { token: await getSessionToken() });
  if (result.status === 404) notFound();

  return <SimProfile id={Number(id)} />;
}
