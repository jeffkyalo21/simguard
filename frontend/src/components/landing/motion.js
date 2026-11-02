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
