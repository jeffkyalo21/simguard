"use client";

import { useCallback, useEffect, useRef, useState } from "react";

/**
 * Loads `load()` now and again every `intervalMs`, but not while the browser
 * tab is hidden; coming back to the tab refreshes at once. `refresh()` loads
 * on demand. A failed refresh keeps the last data and sets `error`; the next
 * success clears it. Pass `intervalMs` as null to stop polling.
 * @template T
 * @param {() => Promise<T>} load
 * @param {number | null} intervalMs
 * @returns {{ data: T | null, error: unknown, loading: boolean, refresh: () => Promise<void> }}
 */
export function usePolling(load, intervalMs) {
  const [state, setState] = useState({ data: null, error: null, loading: true });
  const loadRef = useRef(load);

  useEffect(() => {
    loadRef.current = load;
  });

  const refresh = useCallback(async () => {
    try {
      const data = await loadRef.current();
      setState({ data, error: null, loading: false });
    } catch (error) {
      setState((previous) => ({ ...previous, error, loading: false }));
    }
  }, []);

  useEffect(() => {
    const first = setTimeout(refresh, 0);
    const onVisible = () => {
      if (!document.hidden) refresh();
    };
    document.addEventListener("visibilitychange", onVisible);
    const timer =
      intervalMs == null
        ? null
        : setInterval(() => {
            if (!document.hidden) refresh();
          }, intervalMs);

    return () => {
      clearTimeout(first);
      clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [intervalMs, refresh]);

  return { ...state, refresh };
}
