"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { EmptyState } from "@/components/shared/empty-state";
import { ErrorState } from "@/components/shared/error-state";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";
import { copy } from "@/lib/copy";

/**
 * @typedef {Object} Column
 * @property {string} key
 * @property {string} header
 * @property {(row: any) => React.ReactNode} cell
 * @property {boolean} [link]       cell becomes the row's link (keyboard path)
 * @property {boolean} [srOnlyHeader]
 * @property {string} [className]
 */

const INTERACTIVE = "a, button, input, select, [role='menuitem'], [role='checkbox']";

/**
 * The one table pattern: strong glass, sticky header, horizontal scroll
 * inside its surface, skeleton rows, error and empty rows.
 * Rows with `getRowHref` open on click; the `link` column carries the real
 * link so keyboard and screen reader users get the same path.
 * @param {{ caption: string, columns: Column[], rows: any[] | null,
 *   getRowKey: (row: any) => React.Key, getRowHref?: (row: any) => string,
 *   error?: unknown, onRetry?: () => void, busy?: boolean,
 *   emptyMessage: string, emptyAction?: React.ReactNode,
 *   skeletonRows?: number, scroll?: boolean, className?: string }} props
 */
export function DataTable({
  caption,
  columns,
  rows,
  getRowKey,
  getRowHref,
  error,
  onRetry,
  busy = false,
  emptyMessage,
  emptyAction,
  skeletonRows = 5,
  scroll = false,
  className,
}) {
  const router = useRouter();

  function body() {
    if (error) {
      return (
        <tr>
          <td colSpan={columns.length}>
            <ErrorState onRetry={onRetry} />
          </td>
        </tr>
      );
    }

    if (!rows) {
      return Array.from({ length: skeletonRows }, (_, index) => (
        <tr key={index} className="border-b border-border last:border-0">
          {columns.map((column) => (
            <td key={column.key} className="h-[52px] px-5">
              <Skeleton className="h-4 w-full max-w-28" />
            </td>
          ))}
        </tr>
      ));
    }

    if (!rows.length) {
      return (
        <tr>
          <td colSpan={columns.length}>
            <EmptyState message={emptyMessage} action={emptyAction} className="py-12" />
          </td>
        </tr>
      );
    }

    return rows.map((row) => {
      const href = getRowHref?.(row);

      return (
        <tr
          key={getRowKey(row)}
          onClick={
            href
              ? (event) => {
                  if (!event.target.closest(INTERACTIVE)) router.push(href);
                }
              : undefined
          }
          className={cn(
            "border-b border-border transition-colors duration-150 last:border-0 hover:bg-foreground/[0.04]",
            href && "cursor-pointer"
          )}
        >
          {columns.map((column) => (
            <td key={column.key} className={cn("h-[52px] px-5 whitespace-nowrap", column.className)}>
              {column.link && href ? (
                <Link href={href} className="rounded-sm font-medium hover:underline underline-offset-4">
                  {column.cell(row)}
                </Link>
              ) : (
                column.cell(row)
              )}
            </td>
          ))}
        </tr>
      );
    });
  }

  return (
    <div
      className={cn(
        "w-full min-w-0 overflow-x-auto",
        scroll && "max-h-[min(70vh,720px)] overflow-y-auto",
        className
      )}
      // A focusable scroll region lets keyboard users scroll it.
      tabIndex={0}
      role="region"
      aria-label={caption}
    >
      <table className="w-full border-collapse text-sm" aria-busy={busy || !rows || undefined}>
        <caption className="sr-only">{caption}</caption>
        <thead>
          <tr className="border-b border-border">
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={cn(
                  "sticky top-0 z-10 h-11 bg-glass-strong px-5 text-left text-xs font-medium whitespace-nowrap text-muted-foreground",
                  column.className
                )}
              >
                <span className={column.srOnlyHeader ? "sr-only" : undefined}>{column.header}</span>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>{body()}</tbody>
      </table>
      {!rows && !error && <span className="sr-only" role="status">{copy.a11y.loading}</span>}
    </div>
  );
}
