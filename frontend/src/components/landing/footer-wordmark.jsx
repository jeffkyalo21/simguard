"use client";

import { useEffect, useRef } from "react";
import { allowsHeavyEffects } from "@/components/landing/motion";
import { copy } from "@/lib/copy";

const SHIFT = 40;

/**
 * The large, faint product name cropped at the bottom of the footer panel.
 * Decoration only. It rises a little as the footer scrolls into view: one
 * transform per frame, off on touch, small screens and reduced motion.
 */
export function FooterWordmark() {
  const ref = useRef(null);

  useEffect(() => {
    const element = ref.current;
    if (!element || !allowsHeavyEffects()) return undefined;

    const panel = element.parentElement;
    let frame = 0;

    function update() {
      frame = 0;
      const rect = panel.getBoundingClientRect();
      const progress = Math.min(Math.max((window.innerHeight - rect.top) / rect.height, 0), 1);
      element.style.transform = `translate3d(0, ${((1 - progress) * SHIFT).toFixed(1)}px, 0)`;
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
  }, []);

  return (
    <div ref={ref} aria-hidden="true" className="footer-wordmark">
      {copy.app.name}
    </div>
  );
}
