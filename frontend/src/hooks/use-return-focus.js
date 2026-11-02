"use client";

import { useLayoutEffect, useRef } from "react";

/**
 * Returns focus to whatever opened a dialog. Radix only refocuses its own
 * DialogTrigger, and our dialogs open from state (a button or a row menu),
 * so without this focus falls back to <body> on close.
 * Pass the result as DialogContent's onCloseAutoFocus.
 * @param {boolean} open
 * @returns {(event: Event) => void}
 */
export function useReturnFocus(open) {
  const opener = useRef(null);

  // Runs before the dialog moves focus inside itself.
  useLayoutEffect(() => {
    if (!open) return;
    let element = document.activeElement;
    // A menu item is gone once the menu closes: use the menu's trigger.
    const menu = element?.closest?.("[role=menu]");
    if (menu?.id) element = document.querySelector(`[aria-controls="${menu.id}"]`) ?? element;
    opener.current = element;
  }, [open]);

  return (event) => {
    const element = opener.current;
    if (element?.isConnected && element !== document.body) {
      event.preventDefault();
      element.focus({ preventScroll: true });
    }
  };
}
