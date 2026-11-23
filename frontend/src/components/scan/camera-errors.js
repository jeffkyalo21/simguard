// What to ask the camera for, in what order, and what to tell the person when
// it fails. Pure functions, no browser APIs, so they can be tested.

/**
 * The requests to try, best first. The first holds the scanner's wishes (rear
 * camera, full HD, continuous focus); the next ones give up wishes until a
 * plain "any camera" request, so a laptop webcam with no rear camera or no
 * focus control still opens.
 */
export function cameraConstraintLadder() {
  return [
    {
      audio: false,
      video: {
        facingMode: { ideal: "environment" },
        width: { ideal: 1920 },
        height: { ideal: 1080 },
        advanced: [{ focusMode: "continuous" }],
      },
    },
    { audio: false, video: { facingMode: { ideal: "environment" } } },
    { audio: false, video: true },
  ];
}

// Errors where a simpler request can still work.
const RETRY_WITH_SIMPLER = new Set([
  "OverconstrainedError",
  "ConstraintNotSatisfiedError",
  "NotReadableError",
  "TrackStartError",
  "AbortError",
  "TypeError",
]);

/** True when the next, simpler request is worth trying after this error. */
export function tryNextConstraint(error) {
  return RETRY_WITH_SIMPLER.has(error?.name);
}

/**
 * Why the camera did not open, as a key into the scan messages:
 * "insecure", "blocked", "missing", "inUse", "settings", "reader" or "failed".
 * @param {{ name?: string } | null | undefined} error
 * @param {{ secure?: boolean, hasApi?: boolean }} [page] whether the page is secure and has camera access at all
 */
export function cameraErrorKind(error, { secure = true, hasApi = true } = {}) {
  if (!secure || !hasApi) return "insecure";

  switch (error?.name) {
    case "NotAllowedError":
    case "PermissionDeniedError":
    case "SecurityError":
      return "blocked";
    case "NotFoundError":
    case "DevicesNotFoundError":
      return "missing";
    case "NotReadableError":
    case "TrackStartError":
      return "inUse";
    case "OverconstrainedError":
    case "ConstraintNotSatisfiedError":
      return "settings";
    case "ReaderLoadError":
      return "reader";
    default:
      return "failed";
  }
}
