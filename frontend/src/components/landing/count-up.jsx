"use client";

import { useEffect, useLayoutEffect, useRef } from "react";
import { prefersReducedMotion } from "@/components/landing/motion";

const DURATION_MS = 1200;
const format = new Intl.NumberFormat("en-KE");

/**
 * Counts up to `value` once, the first time it scrolls into view. The server
 * renders the final value (no JavaScript, reduced motion and screen readers
 * all get it); an invisible copy reserves the width, so counting never shifts
 * the layout.
 * @param {{ value: number, prefix?: string, suffix?: string }} props
 */
export function CountUp({ value, prefix = "", suffix = "" }) {
  const ref = useRef(null);
  // Survives the page being hidden and shown again, so it counts only once.
  const done = useRef(false);
  const final = `${prefix}${format.format(value)}${suffix}`;

  // Before paint: start from zero unless motion is reduced.
  useLayoutEffect(() => {
    if (!done.current && !prefersReducedMotion() && ref.current) {
      ref.current.textContent = `${prefix}0${suffix}`;
    }
  }, [prefix, suffix]);

  useEffect(() => {
    const element = ref.current;
    if (!element || done.current || prefersReducedMotion()) return undefined;

    let frame = 0;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting) return;
        observer.disconnect();
        done.current = true;

        const start = performance.now();
        const tick = (now) => {
          const t = Math.min((now - start) / DURATION_MS, 1);
          const eased = 1 - (1 - t) ** 3;
          element.textContent = `${prefix}${format.format(Math.round(value * eased))}${suffix}`;
          if (t < 1) frame = requestAnimationFrame(tick);
        };
        frame = requestAnimationFrame(tick);
      },
      { threshold: 0.5 }
    );

    observer.observe(element);
    return () => {
      observer.disconnect();
      cancelAnimationFrame(frame);
      // Hidden mid-count: show the final value rather than a stale number.
      if (done.current) element.textContent = final;
    };
  }, [value, prefix, suffix, final]);

  return (
    <span className="inline-grid tabular-nums">
      <span className="invisible col-start-1 row-start-1" aria-hidden="true">
        {final}
      </span>
      <span className="sr-only">{final}</span>
      <span ref={ref} className="col-start-1 row-start-1 text-left" aria-hidden="true">
        {final}
      </span>
    </span>
  );
}
