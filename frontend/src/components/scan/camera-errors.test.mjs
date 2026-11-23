// Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { cameraConstraintLadder, cameraErrorKind, tryNextConstraint } from "./camera-errors.js";

const named = (name) => ({ name });

test("every camera error name maps to its own message key", () => {
  assert.equal(cameraErrorKind(named("NotAllowedError")), "blocked");
  assert.equal(cameraErrorKind(named("PermissionDeniedError")), "blocked");
  assert.equal(cameraErrorKind(named("SecurityError")), "blocked");
  assert.equal(cameraErrorKind(named("NotFoundError")), "missing");
  assert.equal(cameraErrorKind(named("DevicesNotFoundError")), "missing");
  assert.equal(cameraErrorKind(named("NotReadableError")), "inUse");
  assert.equal(cameraErrorKind(named("TrackStartError")), "inUse");
  assert.equal(cameraErrorKind(named("OverconstrainedError")), "settings");
  assert.equal(cameraErrorKind(named("ReaderLoadError")), "reader");
  assert.equal(cameraErrorKind(named("AbortError")), "failed");
  assert.equal(cameraErrorKind(new Error("odd")), "failed");
  assert.equal(cameraErrorKind(undefined), "failed");
});

test("an insecure page or a missing camera API wins over the error name", () => {
  assert.equal(cameraErrorKind(named("NotAllowedError"), { secure: false }), "insecure");
  assert.equal(cameraErrorKind(named("NotFoundError"), { hasApi: false }), "insecure");
});

test("the constraint ladder goes from the scanner's wishes down to any camera", () => {
  const [best, rear, any] = cameraConstraintLadder();

  assert.equal(cameraConstraintLadder().length, 3);
  assert.deepEqual(best.video.facingMode, { ideal: "environment" });
  assert.deepEqual(best.video.width, { ideal: 1920 });
  assert.deepEqual(best.video.height, { ideal: 1080 });
  assert.deepEqual(best.video.advanced, [{ focusMode: "continuous" }]);
  assert.deepEqual(rear.video, { facingMode: { ideal: "environment" } });
  assert.equal(any.video, true);
  for (const rung of [best, rear, any]) assert.equal(rung.audio, false);
});

test("each call gives a fresh ladder, so a caller cannot change the next one", () => {
  const first = cameraConstraintLadder();
  first[0].video.width.ideal = 1;
  assert.equal(cameraConstraintLadder()[0].video.width.ideal, 1920);
});

test("a simpler request is tried after constraint and device trouble, not after a refusal", () => {
  for (const name of ["OverconstrainedError", "NotReadableError", "AbortError", "TypeError"]) {
    assert.equal(tryNextConstraint(named(name)), true, name);
  }
  for (const name of ["NotAllowedError", "SecurityError", "NotFoundError"]) {
    assert.equal(tryNextConstraint(named(name)), false, name);
  }
});
