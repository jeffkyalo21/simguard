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
    employees: fixtures.employees,
    /** Activity added by edits in this session: { id, simId, at, kind, detail } */
    activity: [],
    /** GET /audit-logs rows: the seeded history plus every write in this session. */
    audit: fixtures.auditLogs,
  };
}

/** Any action may carry an `audit` row; it is appended after the change. */
export function mockReducer(state, action) {
  const next = applyChange(state, action);
  return action.audit && next !== state ? { ...next, audit: [...next.audit, action.audit] } : next;
}

/**
 * Actions: "sim/saved" inserts or replaces a SIM and logs what happened;
 * "allocation/saved" inserts or replaces an allocation and its SIM;
 * "carrier/saved" inserts or replaces a carrier; "carrier/removed" drops
 * one. Pure: ids and timestamps arrive in the action, and the rules
 * (unique names, carriers in use, one active allocation) are checked in
 * src/lib/data.js.
 */
function applyChange(state, action) {
  switch (action.type) {
    case "allocation/saved": {
      const exists = state.allocations.some((item) => item.id === action.allocation.id);

      return {
        ...state,
        version: state.version + 1,
        allocations: exists
          ? state.allocations.map((item) => (item.id === action.allocation.id ? action.allocation : item))
          : [...state.allocations, action.allocation],
        sims: state.sims.map((sim) => (sim.id === action.sim.id ? action.sim : sim)),
      };
    }
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
    case "alert/changed":
      return {
        ...state,
        version: state.version + 1,
        alerts: state.alerts.map((alert) => alert.id === action.alert.id ? action.alert : alert),
        activity: [...state.activity, action.activity],
      };
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
