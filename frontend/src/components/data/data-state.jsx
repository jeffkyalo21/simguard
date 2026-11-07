import { EmptyState } from "@/components/shared/empty-state";
import { ErrorState } from "@/components/shared/error-state";
import { copy } from "@/lib/copy";

/**
 * One pattern for every data block: skeleton while loading, an error with
 * "Try again", an empty line, or the content.
 * @param {{ resource: { data: unknown, error: unknown, retry: () => void },
 *   skeleton: React.ReactNode, isEmpty?: (data: any) => boolean,
 *   emptyMessage?: string, emptyIcon?: React.ElementType,
 *   children: (data: any) => React.ReactNode }} props
 */
export function DataState({ resource, skeleton, isEmpty, emptyMessage, emptyIcon, children }) {
  const { data, error, retry } = resource;

  if (error) return <ErrorState error={error} onRetry={retry} />;

  if (!data) {
    return (
      <div role="status" aria-busy="true">
        <span className="sr-only">{copy.a11y.loading}</span>
        {skeleton}
      </div>
    );
  }

  if (isEmpty?.(data)) return <EmptyState message={emptyMessage} icon={emptyIcon} className="py-10" />;

  return children(data);
}
