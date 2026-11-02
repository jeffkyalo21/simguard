/**
 * The shared cream backdrop behind every page: a warm base with five soft,
 * out-of-focus shapes (sand, peach, beige, rose, sage). Static by default;
 * only the Landing page renders an `animated` copy on top, whose shapes
 * drift very slowly. Decorative, so hidden from assistive tech.
 * @param {{ animated?: boolean }} props
 */
export function GlassBackdrop({ animated = false }) {
  return (
    <div className="glass-backdrop" data-animated={animated || undefined} aria-hidden="true">
      <span className="glass-shape" />
      <span className="glass-shape" />
      <span className="glass-shape" />
      <span className="glass-shape" />
      <span className="glass-shape" />
    </div>
  );
}
