"use client";

import { useEffect, useRef } from "react";
import { allowsHeavyEffects } from "@/components/landing/motion";
import { cn } from "@/lib/utils";

/**
 * Moves its content against the scroll at `speed` (0.1 is a tenth of the
 * scroll distance). Reads only window.scrollY in a passive listener and
 * writes one transform per frame. Off on touch, small screens and reduced
 * motion.
 * @param {{ speed?: number, className?: string, style?: React.CSSProperties,
 *   children: React.ReactNode }} props
 */
export function Parallax({ speed = 0.1, className, style, children }) {
  const ref = useRef(null);

  useEffect(() => {
    const element = ref.current;
    if (!element || !allowsHeavyEffects()) return undefined;

    let frame = 0;

    function update() {
      frame = 0;
      // Past the hero there is nothing to move.
      const y = Math.min(window.scrollY, window.innerHeight * 1.5);
      element.style.transform = `translate3d(0, ${(-y * speed).toFixed(1)}px, 0)`;
    }

    function onScroll() {
      if (!frame) frame = requestAnimationFrame(update);
    }

    update();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("scroll", onScroll);
    };
  }, [speed]);

  return (
    <div ref={ref} className={cn("will-change-transform", className)} style={style}>
      {children}
    </div>
  );
}
