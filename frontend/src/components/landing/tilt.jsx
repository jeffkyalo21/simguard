"use client";

import { useEffect, useRef } from "react";
import { allowsHeavyEffects } from "@/components/landing/motion";
import { cn } from "@/lib/utils";

const MAX_DEG = 6;

/**
 * Tilts gently toward the pointer (3D, at most 6 degrees). Off on touch,
 * small screens and reduced motion. Pointer moves are batched per frame
 * and only write two CSS variables.
 * @param {{ className?: string, children: React.ReactNode }} props
 */
export function Tilt({ className, children }) {
  const ref = useRef(null);

  useEffect(() => {
    const element = ref.current;
    if (!element || !allowsHeavyEffects()) return undefined;

    let frame = 0;
    let rect = null;

    function onEnter() {
      rect = element.getBoundingClientRect();
      element.dataset.active = "true";
    }

    function onMove(event) {
      if (!rect) return;
      const x = (event.clientX - rect.left) / rect.width - 0.5;
      const y = (event.clientY - rect.top) / rect.height - 0.5;
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(() => {
        element.style.setProperty("--ry", `${(x * MAX_DEG * 2).toFixed(2)}deg`);
        element.style.setProperty("--rx", `${(-y * MAX_DEG * 2).toFixed(2)}deg`);
      });
    }

    function onLeave() {
      cancelAnimationFrame(frame);
      rect = null;
      element.dataset.active = "false";
      element.style.setProperty("--rx", "0deg");
      element.style.setProperty("--ry", "0deg");
    }

    element.addEventListener("pointerenter", onEnter);
    element.addEventListener("pointermove", onMove, { passive: true });
    element.addEventListener("pointerleave", onLeave);
    return () => {
      cancelAnimationFrame(frame);
      element.removeEventListener("pointerenter", onEnter);
      element.removeEventListener("pointermove", onMove);
      element.removeEventListener("pointerleave", onLeave);
    };
  }, []);

  return (
    <div ref={ref} className={cn("tilt", className)}>
      {children}
    </div>
  );
}
