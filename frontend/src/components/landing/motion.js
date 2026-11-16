// Shared checks for the Landing page's motion. Client only.

export function prefersReducedMotion() {
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

/** Tilt and parallax: wide screens with a fine pointer and full motion only. */
export function allowsHeavyEffects() {
  return window.matchMedia(
    "(min-width: 768px) and (hover: hover) and (pointer: fine) and (prefers-reduced-motion: no-preference)"
  ).matches;
}

let gsapPromise = null;

/**
 * Loads GSAP, ScrollTrigger and SplitText once, on demand. Only the Landing page calls
 * this, and always through a dynamic import, so no other route carries GSAP.
 * @returns {Promise<{ gsap: import("gsap").gsap, ScrollTrigger: typeof import("gsap/ScrollTrigger").ScrollTrigger, SplitText: typeof import("gsap/SplitText").SplitText }>}
 */
export function loadGsap() {
  gsapPromise ??= Promise.all([import("gsap"), import("gsap/ScrollTrigger"), import("gsap/SplitText")]).then(
    ([core, trigger, split]) => {
      core.gsap.registerPlugin(trigger.ScrollTrigger, split.SplitText);
      return { gsap: core.gsap, ScrollTrigger: trigger.ScrollTrigger, SplitText: split.SplitText };
    }
  );

  return gsapPromise;
}
