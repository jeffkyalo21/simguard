/**
 * One header pattern for every page: title (with an optional badge) left,
 * actions right.
 * @param {{ title: string, badge?: React.ReactNode, actions?: React.ReactNode }} props
 */
export function PageHeader({ title, badge, actions }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-4">
      <div className="flex min-w-0 flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        {badge}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  );
}
