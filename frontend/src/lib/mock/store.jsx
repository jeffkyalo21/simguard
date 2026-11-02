"use client";

// TEMPORARY (issue #17): React home of the mock database. Removed in #20.

import { createContext, useContext, useLayoutEffect, useReducer, useSyncExternalStore } from "react";
import { bindMockDb, createInitialState, mockReducer } from "@/lib/mock/db";

const MockVersionContext = createContext(0);
const CarriersContext = createContext([]);

const noSubscribe = () => () => {};

// Dev aid: ?mock=empty also empties the carriers list, to check the
// "No carriers yet" state. Read after hydration so server HTML matches.
function useEmptyMock() {
  return useSyncExternalStore(
    noSubscribe,
    () => new URLSearchParams(window.location.search).get("mock") === "empty",
    () => false
  );
}

export function MockStoreProvider({ children }) {
  const [state, dispatch] = useReducer(mockReducer, undefined, createInitialState);
  const carriers = useEmptyMock() ? [] : state.carriers;

  // Layout effects run before children's data effects, so reads see this state.
  useLayoutEffect(() => {
    bindMockDb(state, dispatch);
  }, [state]);

  return (
    <MockVersionContext value={state.version}>
      <CarriersContext value={carriers}>{children}</CarriersContext>
    </MockVersionContext>
  );
}

/** Bumps after every mock write, so screens refetch. */
export function useMockVersion() {
  return useContext(MockVersionContext);
}

/**
 * The carriers list, kept current after adds, renames and removals.
 * #20: replace with a GET /carriers resource.
 * @returns {{ id: number, name: string, status: "connected" | "not connected", createdAt: string }[]}
 */
export function useCarriers() {
  return useContext(CarriersContext);
}

/** Name of a carrier by id, or null when the id is unknown. */
export function carrierName(carriers, id) {
  return carriers.find((carrier) => carrier.id === Number(id))?.name ?? null;
}
