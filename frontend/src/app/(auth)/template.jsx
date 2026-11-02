import { PageTransition } from "@/components/layout/page-transition";

// Same page transition as the app, so arriving at sign-in feels consistent.
export default function AuthTemplate({ children }) {
  return <PageTransition>{children}</PageTransition>;
}
