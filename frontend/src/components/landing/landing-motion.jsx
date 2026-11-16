"use client";

import { useEffect } from "react";
import { loadGsap } from "@/components/landing/motion";

// How long the page waits for GSAP before it shows everything as is.
const LOAD_LIMIT_MS = 4000;

/**
 * Landing page motion driven by GSAP, loaded on demand (landing only):
 * - the headline typewriter, then the subtext and buttons fading in,
 * - the slow drifting glow behind the hero,
 * - section and card reveals that fade up and stagger in on scroll,
 * - the pipeline line that draws itself while each step lights up.
 *
 * Hover lifts, button press and shine are plain CSS
 * (landing.css). Everything runs in one gsap.context and is reverted on
 * unmount. With reduced motion nothing moves and every reveal is already
 * in its final state. Until GSAP is ready the page root carries
 * data-motion="pending", which hides only the below-the-fold start states;
 * if GSAP fails to load, data-motion turns "off" and the page shows as is.
 * @param {{ rootRef: React.RefObject<HTMLElement | null> }} props
 */
export function LandingMotion({ rootRef }) {
  useEffect(() => {
    const root = rootRef.current;
    if (!root) return undefined;

    let cancelled = false;
    let revert = () => {};

    const giveUp = window.setTimeout(() => {
      if (root.dataset.motion === "pending") root.dataset.motion = "off";
    }, LOAD_LIMIT_MS);

    loadGsap()
      .then(async ({ gsap, ScrollTrigger, SplitText }) => {
        // Split the text after the display font is in, so lines break as they will stay.
        await Promise.race([document.fonts.ready, new Promise((resolve) => window.setTimeout(resolve, 1000))]);
        if (cancelled) return;
        window.clearTimeout(giveUp);

        const ctx = gsap.context(() => {
          const mm = gsap.matchMedia();

          mm.add("(prefers-reduced-motion: no-preference)", () => {
            // Headline: SplitText splits it into characters and one timeline
            // types them in once; the subtext and buttons fade in after it.
            const text = root.querySelector("[data-type]");
            const cursor = root.querySelector("[data-cursor]");
            const bar = root.querySelector("[data-cursor-bar]");
            const fades = gsap.utils.toArray(".hero-fade", root);

            gsap.set(fades, { opacity: 0, y: 10 });

            if (text && cursor && bar) {
              const delay = 0.3;
              const step = 0.04;
              // Every character keeps its space; only its opacity changes.
              const split = SplitText.create(text, { type: "chars", charsClass: "type-char", aria: "none" });
              const total = split.chars.length;
              const typedAt = delay + total * step;
              const typed = { count: 0 };
              let shown = 0;

              gsap.set(split.chars, { opacity: 0 });
              gsap.set(text, { opacity: 1 });
              const place = (char, after) =>
                gsap.set(cursor, {
                  x: char.offsetLeft + (after ? char.offsetWidth : 0),
                  y: char.offsetTop + char.offsetHeight * 0.1,
                  height: char.offsetHeight * 0.8,
                });
              place(split.chars[0], false);

              const blink = gsap.to(bar, { opacity: 0, duration: 0.5, ease: "steps(1)", repeat: -1, yoyo: true });

              gsap
                .timeline()
                .set(cursor, { opacity: 1 }, 0)
                .to(
                  typed,
                  {
                    count: total,
                    duration: total * step,
                    ease: `steps(${total})`,
                    onUpdate: () => {
                      const count = Math.round(typed.count);
                      if (count > shown) {
                        gsap.set(split.chars.slice(shown, count), { opacity: 1 });
                        shown = count;
                        place(split.chars[count - 1], true);
                      }
                    },
                  },
                  delay
                )
                // The cursor fades out about 1.5 s after the last character.
                .to(
                  cursor,
                  { opacity: 0, duration: 0.3, onComplete: () => (blink.kill(), split.revert()) },
                  typedAt + 1.5
                )
                .to(
                  fades,
                  { opacity: 1, y: 0, duration: 0.6, ease: "power2.out", stagger: 0.1, clearProps: "opacity,transform" },
                  typedAt + 0.2
                );
            } else {
              gsap.to(fades, { opacity: 1, y: 0, duration: 0.6, stagger: 0.1, clearProps: "opacity,transform" });
            }

            // Hero glow: two soft blobs drift slowly while the hero is on screen.
            const hero = root.querySelector("[data-hero]");
            const drifts = gsap.utils.toArray("[data-glow]", root).map((glow, index) =>
              gsap.to(glow, {
                x: index % 2 ? -70 : 60,
                y: index % 2 ? 40 : -30,
                scale: 1.12,
                duration: index % 2 ? 18 : 14,
                ease: "sine.inOut",
                repeat: -1,
                yoyo: true,
              })
            );

            if (hero) {
              ScrollTrigger.create({
                trigger: hero,
                start: "top bottom",
                end: "bottom top",
                onToggle: (self) => drifts.forEach((drift) => drift.paused(!self.isActive)),
              });
            }

            // Reveals: each fades up as it enters; siblings that enter together stagger.
            const reveals = gsap.utils.toArray(".reveal", root);
            gsap.set(reveals, { opacity: 0, y: 16 });
            ScrollTrigger.batch(reveals, {
              start: "top 88%",
              once: true,
              onEnter: (batch) =>
                gsap.to(batch, {
                  opacity: 1,
                  y: 0,
                  duration: 0.6,
                  ease: "power2.out",
                  stagger: 0.08,
                  overwrite: true,
                  // Hand the transform back to the CSS hover lift.
                  clearProps: "opacity,transform",
                }),
            });

            // Pipeline: the line draws with the scroll, each card lights up when reached.
            const steps = root.querySelector("[data-steps]");
            if (steps) {
              const line = steps.querySelector("[data-steps-line]");
              const items = gsap.utils.toArray("[data-step]", steps);
              const last = items[items.length - 1];

              // The line ends at the centre of the last numbered circle.
              const sizeEnd = () => steps.style.setProperty("--last-step", `${last.offsetHeight}px`);
              sizeEnd();
              ScrollTrigger.addEventListener("refresh", sizeEnd);

              gsap.fromTo(
                line,
                { scaleY: 0, transformOrigin: "top" },
                {
                  scaleY: 1,
                  ease: "none",
                  scrollTrigger: {
                    trigger: steps,
                    start: "top 60%",
                    end: () => `+=${Math.max(steps.offsetHeight - last.offsetHeight + 20, 1)}`,
                    scrub: 0.4,
                    invalidateOnRefresh: true,
                  },
                }
              );

              items.forEach((item) =>
                ScrollTrigger.create({
                  trigger: item,
                  start: "top 65%",
                  onEnter: () => (item.dataset.active = "true"),
                  onLeaveBack: () => (item.dataset.active = "false"),
                })
              );

              root.dataset.motion = "on";
              return () => ScrollTrigger.removeEventListener("refresh", sizeEnd);
            }

            root.dataset.motion = "on";
            return undefined;
          });

          mm.add("(prefers-reduced-motion: reduce)", () => {
            // Final state at once: no motion, every step lit.
            root.dataset.motion = "off";
            root.querySelectorAll("[data-step]").forEach((item) => (item.dataset.active = "true"));
          });
        }, root);

        revert = () => ctx.revert();
      })
      .catch(() => {
        root.dataset.motion = "off";
      });

    return () => {
      cancelled = true;
      window.clearTimeout(giveUp);
      revert();
    };
  }, [rootRef]);

  return null;
}
