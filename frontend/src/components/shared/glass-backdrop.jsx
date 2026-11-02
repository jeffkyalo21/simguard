/**
 * The shared gradient and two orbs behind every page. Static by default;
 * only the Landing page renders an `animated` copy on top (slow drifting
 * orbs). Decorative, so hidden from assistive tech.
 * @param {{ animated?: boolean }} props
 */
export function GlassBackdrop({ animated = false }) {
  return <div className="glass-backdrop" data-animated={animated || undefined} aria-hidden="true" />;
}
