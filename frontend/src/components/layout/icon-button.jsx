import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

/**
 * Round icon-only button: 40px on desktop, 44px on touch screens. The
 * label is its accessible name, so the icon stays decorative.
 * @param {{ label: string, className?: string, children: React.ReactNode }
 *   & React.ComponentProps<typeof Button>} props
 */
export function IconButton({ label, className, children, ...props }) {
  return (
    <Button
      variant="ghost"
      size="icon"
      aria-label={label}
      className={cn("icon-button relative size-10 rounded-full [&_svg:not([class*='size-'])]:size-[18px]", className)}
      {...props}
    >
      {children}
    </Button>
  );
}
