"use client";

import { useEffect, useEffectEvent, useState } from "react";
import { useMockVersion } from "@/lib/mock/store";

/**
 * Loads data from a src/lib/data.js function and tracks its state.
 * `key` lists the inputs; when it changes the old data is dropped and the
 * skeleton shows. After a write elsewhere (the mock version bumps) the data
 * reloads in place, without a skeleton.
 * #20: swap useMockVersion for an explicit invalidation signal.
 *
 * @template T
 * @param {() => Promise<T>} load
 * @param {unknown[]} key
 * @returns {{ data: T | null, error: unknown, loading: boolean, retry: () => void }}
 */
export function useResource(load, key) {
  const version = useMockVersion();
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState({ inputs: null, data: null, error: null });

  const inputs = JSON.stringify([key, attempt]);
  const request = `${inputs}|${version}`;
  const runLoad = useEffectEvent(load);

  useEffect(() => {
    let active = true;

    runLoad().then(
      (data) => active && setResult({ inputs, request, data, error: null }),
      (error) => active && setResult({ inputs, request, data: null, error })
    );

    return () => {
      active = false;
    };
  }, [inputs, request]);

  const sameInputs = result.inputs === inputs;

  return {
    data: sameInputs ? result.data : null,
    error: sameInputs ? result.error : null,
    loading: result.request !== request,
    retry: () => setAttempt((value) => value + 1),
  };
}
