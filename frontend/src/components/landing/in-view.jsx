"use client";

import { useEffect, useRef } from "react";
import { cn } from "@/lib/utils";

/**
 * Marks its element from an IntersectionObserver, written to the DOM
 * directly so scrolling never re-renders React.
 * - Default: data-inview turns "true" on first sighting and stays (reveals,
 *   counts and fills play once).
 * - `track`: data-onscreen follows visibility (loops pause off screen).
 * @param {{ as?: React.ElementType, track?: boolean, threshold?: number,
 *   className?: string, children: React.ReactNode } & Record<string, unknown>} props
 */
export function InView({ as: Component = "div", track = false, threshold = 0.25, className, children, ...props }) {
  const ref = useRef(null);

  useEffect(() => {
    const element = ref.current;
    if (!element) return undefined;

    const observer = new IntersectionObserver(
      ([entry]) => {
        if (track) {
          element.dataset.onscreen = String(entry.isIntersecting);
        } else if (entry.isIntersecting) {
          element.dataset.inview = "true";
          observer.disconnect();
        }
      },
      { threshold }
    );

    observer.observe(element);
    return () => observer.disconnect();
  }, [track, threshold]);

  // Rendered "false" so the start state holds from the first paint. The CSS
  // only hides anything under `scripting: enabled`, so without JavaScript
  // the content is simply visible.
  return (
    <Component ref={ref} data-inview={track ? undefined : "false"} className={cn(className)} {...props}>
      {children}
    </Component>
  );
}
