"use client";

// A counter that every successful write in src/lib/data.js bumps. Screens
// read data through useResource, which reloads in place when it changes, so
// a list, a profile and the dashboard all show the result of a write without
// a page reload. Nothing here holds data.

import { useSyncExternalStore } from "react";

let version = 0;
const listeners = new Set();

function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

const read = () => version;
const readOnServer = () => 0;

/** Called after a write succeeds. */
export function bumpDataVersion() {
  version += 1;
  for (const listener of listeners) listener();
}

/** The current write count; changes after every successful write. */
export function useDataVersion() {
  return useSyncExternalStore(subscribe, read, readOnServer);
}
