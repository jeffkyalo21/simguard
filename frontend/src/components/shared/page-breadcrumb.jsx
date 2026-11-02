import { Fragment } from "react";
import Link from "next/link";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";

/**
 * "SIMs / +254 712 345 678". The last item is the current page.
 * @param {{ items: { label: string, href?: string }[] }} props
 */
export function PageBreadcrumb({ items }) {
  return (
    <Breadcrumb>
      <BreadcrumbList>
        {items.map(({ label, href }, index) => {
          const last = index === items.length - 1;

          return (
            <Fragment key={label}>
              <BreadcrumbItem>
                {last ? (
                  <BreadcrumbPage>{label}</BreadcrumbPage>
                ) : (
                  <BreadcrumbLink asChild>
                    <Link href={href} className="rounded-sm">{label}</Link>
                  </BreadcrumbLink>
                )}
              </BreadcrumbItem>
              {!last && <BreadcrumbSeparator>/</BreadcrumbSeparator>}
            </Fragment>
          );
        })}
      </BreadcrumbList>
    </Breadcrumb>
  );
}
