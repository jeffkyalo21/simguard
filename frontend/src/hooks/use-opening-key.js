"use client";

import { useState } from "react";

/**
 * A number that changes each time `open` turns true. Use it as a React key
 * so a dialog's form starts fresh on every opening, while closing keeps the
 * key and the exit transition still plays.
 * @param {boolean} open
 */
export function useOpeningKey(open) {
  const [opening, setOpening] = useState(0);
  const [wasOpen, setWasOpen] = useState(open);

  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) setOpening((value) => value + 1);
  }

  return opening;
}
