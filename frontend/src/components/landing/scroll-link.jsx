"use client";

import { prefersReducedMotion } from "@/components/landing/motion";

/**
 * Scrolls to a section (smoothly unless motion is reduced), updates the
 * hash and moves focus to the section's heading. Returns false when the
 * section is missing.
 * @param {string} href "#id"
 */
export function scrollToSection(href) {
  const section = document.getElementById(href.slice(1));
  if (!section) return false;

  section.scrollIntoView({ behavior: prefersReducedMotion() ? "auto" : "smooth", block: "start" });
  history.pushState(null, "", href);
  const target = section.querySelector("h2[tabindex]") ?? section;
  target.focus({ preventScroll: true });
  return true;
}

/**
 * In-page link that uses scrollToSection. Without JavaScript it is a plain
 * #anchor link. Spreads its other props so it can sit inside
 * <Button asChild>.
 * @param {{ href: string, children: React.ReactNode } & React.AnchorHTMLAttributes<HTMLAnchorElement>} props
 */
export function ScrollLink({ href, onClick, children, ...props }) {
  function handleClick(event) {
    onClick?.(event);
    if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    if (scrollToSection(href)) event.preventDefault();
  }

  return (
    <a href={href} {...props} onClick={handleClick}>
      {children}
    </a>
  );
}
