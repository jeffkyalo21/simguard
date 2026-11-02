/**
 * One header pattern for every page: title (with an optional badge) left,
 * actions right. Loading shells pass `as="p"` so a page never holds two h1s
 * while its real header streams in.
 * @param {{ title: string, badge?: React.ReactNode, actions?: React.ReactNode,
 *   as?: "h1" | "p" }} props
 */
export function PageHeader({ title, badge, actions, as: Title = "h1" }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-4">
      <div className="flex min-w-0 flex-wrap items-center gap-3">
        <Title className="text-2xl font-semibold tracking-tight">{title}</Title>
        {badge}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
