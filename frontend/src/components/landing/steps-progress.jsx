"use client";

import { useEffect, useRef } from "react";
import { prefersReducedMotion } from "@/components/landing/motion";

/**
 * Scroll-linked progress for "How it works": the line fills as the list
 * passes the middle of the screen and each step activates once in turn.
 * Position and height are cached on resize; scrolling reads only scrollY.
 * Without JavaScript or with reduced motion, the line is full and every
 * step is active.
 * @param {{ children: React.ReactNode }} props
 */
export function StepsProgress({ children }) {
  const ref = useRef(null);

  useEffect(() => {
    const root = ref.current;
    const line = root.querySelector("[data-steps-line]");
    const steps = [...root.querySelectorAll("[data-step]")];

    if (prefersReducedMotion()) {
      steps.forEach((step) => (step.dataset.active = "true"));
      return undefined;
    }

    let top = 0;
    let height = 1;
    let frame = 0;
    let reached = 0;

    function measure() {
      const rect = root.getBoundingClientRect();
      top = rect.top + window.scrollY;
      height = Math.max(rect.height, 1);
      update();
    }

    function update() {
      frame = 0;
      const progress = Math.min(Math.max((window.scrollY + window.innerHeight * 0.6 - top) / height, 0), 1);
      line.style.setProperty("--progress", progress.toFixed(4));

      // Steps activate once, in order.
      while (reached < steps.length && progress >= (reached + 0.35) / steps.length) {
        steps[reached].dataset.active = "true";
        reached += 1;
      }
    }

    function onScroll() {
      if (!frame) frame = requestAnimationFrame(update);
    }

    const resizeObserver = new ResizeObserver(measure);
    resizeObserver.observe(document.documentElement);
    window.addEventListener("scroll", onScroll, { passive: true });

    return () => {
      cancelAnimationFrame(frame);
      resizeObserver.disconnect();
      window.removeEventListener("scroll", onScroll);
    };
  }, []);

  return (
    <div ref={ref} className="relative">
      {children}
    </div>
  );
}
