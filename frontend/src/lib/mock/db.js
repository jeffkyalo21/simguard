// TEMPORARY (issue #17): in-memory mock database. Nothing is persisted; a
// reload restores the fixtures. Removed in #20 together with src/lib/mock.
//
// The reducer state lives in React (MockStoreProvider in the (app) layout)
// so edits survive navigation. The provider binds that state here, which
// lets the plain async functions in src/lib/data.js read and write it.

import { fixtures } from "@/lib/mock/fixtures";

export function createInitialState() {
  return {
    version: 0,
    /** { id, name, status: "connected" | "not connected", createdAt } */
    carriers: fixtures.carriers,
    sims: fixtures.sims,
    allocations: fixtures.allocations,
    alerts: fixtures.alerts,
    /** Activity added by edits in this session: { id, simId, at, kind, detail } */
    activity: [],
  };
}

/**
 * Actions: "sim/saved" inserts or replaces a SIM and logs what happened;
 * "carrier/saved" inserts or replaces a carrier; "carrier/removed" drops
 * one. Pure: ids and timestamps arrive in the action, and the rules
 * (unique names, carriers in use) are checked in src/lib/data.js.
 */
export function mockReducer(state, action) {
  switch (action.type) {
    case "sim/saved": {
      const exists = state.sims.some((sim) => sim.id === action.sim.id);

      return {
        ...state,
        version: state.version + 1,
        sims: exists
          ? state.sims.map((sim) => (sim.id === action.sim.id ? action.sim : sim))
          : [...state.sims, action.sim],
        activity: [...state.activity, action.activity],
      };
    }
    case "carrier/saved": {
      const exists = state.carriers.some((carrier) => carrier.id === action.carrier.id);

      return {
        ...state,
        version: state.version + 1,
        carriers: exists
          ? state.carriers.map((carrier) => (carrier.id === action.carrier.id ? action.carrier : carrier))
          : [...state.carriers, action.carrier],
      };
    }
    case "carrier/removed":
      return {
        ...state,
        version: state.version + 1,
        carriers: state.carriers.filter((carrier) => carrier.id !== action.id),
      };
    default:
      return state;
  }
}

let current = { state: null, dispatch: null };

/** Called by MockStoreProvider after every commit. */
export function bindMockDb(state, dispatch) {
  current = { state, dispatch };
}

export function readMockDb() {
  if (!current.state) current.state = createInitialState();
  return current.state;
}

/** Applies the action here at once and in React on the next render. */
export function writeMockDb(action) {
  current.state = mockReducer(readMockDb(), action);
  current.dispatch?.(action);
}
