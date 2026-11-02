import { Button } from "@/components/ui/button";
import { copy } from "@/lib/copy";

/**
 * "1 to 20 of 120" with Previous and Next.
 * @param {{ page: number, pageSize: number, total: number,
 *   onPageChange: (page: number) => void }} props
 */
export function Pagination({ page, pageSize, total, onPageChange }) {
  const from = total ? (page - 1) * pageSize + 1 : 0;
  const to = Math.min(page * pageSize, total);
  const lastPage = Math.max(Math.ceil(total / pageSize), 1);

  return (
    <nav aria-label={copy.a11y.pagination} className="flex flex-wrap items-center justify-between gap-3">
      <p className="text-sm text-muted-foreground tabular-nums" aria-live="polite">
        {copy.pagination.range(from, to, total)}
      </p>
      <div className="flex gap-2">
        <Button
          variant="outline"
          className="touch-target h-10 px-4"
          disabled={page <= 1}
          onClick={() => onPageChange(page - 1)}
        >
          {copy.actions.previous}
        </Button>
        <Button
          variant="outline"
          className="touch-target h-10 px-4"
          disabled={page >= lastPage}
          onClick={() => onPageChange(page + 1)}
        >
          {copy.actions.next}
        </Button>
      </div>
    </nav>
  );
}
