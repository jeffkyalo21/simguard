import { notFound } from "next/navigation";
import { SimProfile } from "@/components/sims/sim-profile";
import { isPossibleSimId } from "@/lib/data";
import { copy } from "@/lib/copy";

export const metadata = { title: copy.pages.simDetail.title };

// The page reads its id before rendering, so it may block navigation rather
// than show an instant shell. notFound() runs before anything streams, so an
// unknown id gets a real 404. Keep Suspense boundaries (and loading.jsx)
// out of this route's parents, or the status is fixed at 200.
export const instant = false;

export default async function SimDetailPage({ params }) {
  const { id } = await params;

  // Ids outside the mock range never reach the client.
  if (!isPossibleSimId(id)) notFound();

  return <SimProfile id={Number(id)} />;
}
