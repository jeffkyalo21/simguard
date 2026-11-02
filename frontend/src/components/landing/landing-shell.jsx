"use client";

import { useEffect, useRef } from "react";
import { prefersReducedMotion } from "@/components/landing/motion";

/**
 * Page-level effects for the Landing page, kept in one small island:
 * - the accent scroll-progress bar (scaleX, one write per frame; the page
 *   height is cached on resize, so scrolling never reads layout),
 * - data-scrolled on the nav once the page moves (IntersectionObserver),
 * - data-paused on the page while the tab is hidden.
 * @param {{ children: React.ReactNode }} props
 */
export function LandingShell({ children }) {
  const rootRef = useRef(null);
  const barRef = useRef(null);
  const sentinelRef = useRef(null);

  useEffect(() => {
    const root = rootRef.current;
    const bar = barRef.current;
    const nav = root.querySelector("[data-landing-nav]");
    let scrollable = 1;
    let frame = 0;

    function measure() {
      scrollable = Math.max(document.documentElement.scrollHeight - window.innerHeight, 1);
      update();
    }

    function update() {
      frame = 0;
      const progress = Math.min(window.scrollY / scrollable, 1);
      bar.style.transform = `scaleX(${progress.toFixed(4)})`;
    }

    function onScroll() {
      if (!frame) frame = requestAnimationFrame(update);
    }

    const resizeObserver = new ResizeObserver(measure);
    resizeObserver.observe(document.documentElement);
    window.addEventListener("scroll", onScroll, { passive: true });

    const sentinelObserver = new IntersectionObserver(([entry]) => {
      if (nav) nav.dataset.scrolled = String(!entry.isIntersecting);
    });
    sentinelObserver.observe(sentinelRef.current);

    function onVisibility() {
      if (document.hidden) root.dataset.paused = "";
      else delete root.dataset.paused;
    }
    document.addEventListener("visibilitychange", onVisibility);

    if (prefersReducedMotion()) bar.style.transition = "none";

    return () => {
      cancelAnimationFrame(frame);
      resizeObserver.disconnect();
      sentinelObserver.disconnect();
      window.removeEventListener("scroll", onScroll);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, []);

  return (
    <div ref={rootRef} className="landing relative flex min-h-dvh flex-col">
      <div ref={sentinelRef} className="pointer-events-none absolute top-0 h-6 w-px" aria-hidden="true" />
      <div
        ref={barRef}
        role="presentation"
        aria-hidden="true"
        className="landing-progress fixed inset-x-0 top-0 z-50 h-[3px] bg-brand"
      />
      {children}
    </div>
  );
}
