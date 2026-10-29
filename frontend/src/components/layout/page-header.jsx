/**
 * One header pattern for every page: title left, actions right.
 * @param {{ title: string, actions?: React.ReactNode }} props
 */
export function PageHeader({ title, actions }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-4">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </div>
  );
}
