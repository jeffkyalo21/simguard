"use client";

// TEMPORARY (issue #17): React home of the mock database that the part 1
// write functions still change. Its version makes screens refetch after a
// write. Removed in part 2 of issue #14, step 5.

import { createContext, useContext, useLayoutEffect, useReducer } from "react";
import { bindMockDb, createInitialState, mockReducer } from "@/lib/mock/db";

const MockVersionContext = createContext(0);

export function MockStoreProvider({ children }) {
  const [state, dispatch] = useReducer(mockReducer, undefined, createInitialState);

  // Layout effects run before children's data effects, so reads see this state.
  useLayoutEffect(() => {
    bindMockDb(state, dispatch);
  }, [state]);

  return (
    <MockVersionContext value={state.version}>{children}</MockVersionContext>
  );
}

/** Bumps after every mock write, so screens refetch. */
export function useMockVersion() {
  return useContext(MockVersionContext);
}
