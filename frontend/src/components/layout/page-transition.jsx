import { cn } from "@/lib/utils";

/**
 * The only page transition: content fades in and rises 6px (250 ms,
 * transform and opacity only). Route templates re-mount on navigation, so
 * wrapping a template's children replays it while the shell stays put.
 * @param {{ className?: string, children: React.ReactNode }} props
 */
export function PageTransition({ className, children }) {
  return <div className={cn("page-enter", className)}>{children}</div>;
}
