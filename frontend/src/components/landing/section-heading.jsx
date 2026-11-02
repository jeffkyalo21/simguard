import { cn } from "@/lib/utils";

/**
 * A section's h2 and one line intro. The heading takes focus (tabIndex -1)
 * when a nav link scrolls to its section, so keyboard and screen reader
 * users land on it.
 * @param {{ id: string, title: string, intro: string, className?: string }} props
 */
export function SectionHeading({ id, title, intro, className }) {
  return (
    <div className={cn("reveal mx-auto flex max-w-2xl flex-col gap-3 text-center", className)}>
      <h2 id={id} tabIndex={-1} className="text-3xl font-semibold tracking-tight text-balance outline-none sm:text-4xl">
        {title}
      </h2>
      <p className="text-lg text-muted-foreground text-pretty">{intro}</p>
    </div>
  );
}
