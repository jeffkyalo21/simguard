import { PageTransition } from "@/components/layout/page-transition";

// Re-mounts on every navigation, so each page gets the fade and slide-up.
export default function AppTemplate({ children }) {
  return <PageTransition className="flex flex-col gap-6">{children}</PageTransition>;
}
