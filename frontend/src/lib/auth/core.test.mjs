// Run with `npm test` (Node's built-in test runner, no extra packages).
import { test } from "node:test";
import assert from "node:assert/strict";
import {
  ERROR_CODES,
  PENDING_KINDS,
  cookieOptions,
  decodePending,
  encodePending,
  forwardablePath,
  homeFor,
  isSameOriginRequest,
  mapBackendError,
  parseRetryAfter,
  safeNextPath,
} from "./core.js";

test("safeNextPath keeps relative same-origin paths", () => {
  assert.equal(safeNextPath("/sims"), "/sims");
  assert.equal(safeNextPath("/sims/12?tab=usage#top"), "/sims/12?tab=usage#top");
  assert.equal(safeNextPath("/alerts?status=flagged"), "/alerts?status=flagged");
});

test("safeNextPath rejects open redirects and odd input", () => {
  for (const value of [
    "https://evil.example",
    "//evil.example",
    "/\\evil.example",
    "\\\\evil.example",
    "javascript:alert(1)",
    "sims",
    "/sims\n",
    "/%0d%0aSet-Cookie:x",
    "",
    null,
    undefined,
    42,
    "/api/auth/logout",
    "/login",
    "/login/mfa",
  ]) {
    const result = safeNextPath(value);
    if (value === "/%0d%0aSet-Cookie:x") {
      // Encoded characters stay encoded inside the same site, which is safe.
      assert.equal(result, "/%0d%0aSet-Cookie:x");
    } else {
      assert.equal(result, null, `expected null for ${JSON.stringify(value)}`);
    }
  }
});

test("isSameOriginRequest checks Origin against Host for writes", () => {
  assert.equal(isSameOriginRequest({ method: "GET", host: "localhost:3000" }), true);
  assert.equal(
    isSameOriginRequest({ method: "POST", origin: "http://localhost:3000", host: "localhost:3000" }),
    true
  );
  assert.equal(
    isSameOriginRequest({ method: "POST", origin: "http://evil.example", host: "localhost:3000" }),
    false
  );
  assert.equal(isSameOriginRequest({ method: "DELETE", origin: "null", host: "localhost:3000" }), false);
  assert.equal(isSameOriginRequest({ method: "POST", host: "localhost:3000" }), false);
  assert.equal(
    isSameOriginRequest({ method: "POST", host: "localhost:3000", secFetchSite: "same-origin" }),
    true
  );
  assert.equal(
    isSameOriginRequest({ method: "PATCH", host: "localhost:3000", secFetchSite: "cross-site" }),
    false
  );
});

test("cookieOptions never outlives the token and is Secure off localhost", () => {
  const now = Date.parse("2026-11-07T10:00:00Z");
  const local = cookieOptions({
    hostname: "localhost",
    expiresAt: "2026-11-07T10:30:00Z",
    capSeconds: 3600,
    now,
  });
  assert.deepEqual(local, { httpOnly: true, sameSite: "lax", secure: false, path: "/", maxAge: 1800 });

  const remote = cookieOptions({ hostname: "simguard.example", expiresAt: "2026-11-07T12:00:00Z", capSeconds: 3600, now });
  assert.equal(remote.secure, true);
  assert.equal(remote.maxAge, 3600);

  const expired = cookieOptions({ hostname: "localhost", expiresAt: "2026-11-07T09:00:00Z", capSeconds: 300, now });
  assert.equal(expired.maxAge, 0);

  assert.equal(cookieOptions({ hostname: "127.0.0.1", capSeconds: 300, now }).maxAge, 300);
});

test("pending cookie round trip", () => {
  const value = encodePending(PENDING_KINDS.setup, "a.b.c");
  assert.deepEqual(decodePending(value), { kind: "setup", token: "a.b.c" });
  assert.equal(decodePending("other:a.b.c"), null);
  assert.equal(decodePending("challenge:"), null);
  assert.equal(decodePending(undefined), null);
});

test("mapBackendError gives stable codes", () => {
  assert.deepEqual(mapBackendError({ status: 401, context: "login" }), { code: ERROR_CODES.invalidCredentials });
  assert.deepEqual(mapBackendError({ status: 401, context: "mfa" }), { code: ERROR_CODES.mfaInvalid });
  assert.deepEqual(mapBackendError({ status: 401, context: "enroll" }), { code: ERROR_CODES.challengeExpired });
  assert.deepEqual(mapBackendError({ status: 409, context: "enroll" }), { code: ERROR_CODES.challengeExpired });
  assert.deepEqual(mapBackendError({ status: 401 }), { code: ERROR_CODES.unauthorized });
  assert.deepEqual(
    mapBackendError({ status: 401, context: "mfa", problem: { detail: "The account has no role. Ask an Admin to assign one." } }),
    { code: ERROR_CODES.noRole }
  );
  assert.deepEqual(
    mapBackendError({ status: 400, context: "enroll", problem: { errors: { code: ["bad"] } } }),
    { code: ERROR_CODES.mfaInvalid }
  );
  assert.deepEqual(
    mapBackendError({ status: 400, problem: { errors: { currentPassword: ["x"] } } }),
    { code: ERROR_CODES.validation, fields: { currentPassword: ["x"] } }
  );
  assert.deepEqual(mapBackendError({ status: 429 }), { code: ERROR_CODES.rateLimited });
  assert.deepEqual(mapBackendError({ status: 429, retryAfter: "42" }), { code: ERROR_CODES.rateLimited, retryAfter: 42 });
  assert.deepEqual(mapBackendError({ status: 0 }), { code: ERROR_CODES.network });
  assert.deepEqual(mapBackendError({ status: 503 }), { code: ERROR_CODES.server });
  assert.deepEqual(mapBackendError({ status: 403 }), { code: ERROR_CODES.forbidden });
});

test("parseRetryAfter reads seconds and dates", () => {
  const now = Date.parse("2026-11-07T10:00:00Z");
  assert.equal(parseRetryAfter("30", now), 30);
  assert.equal(parseRetryAfter("Sat, 07 Nov 2026 10:01:00 GMT", now), 60);
  assert.equal(parseRetryAfter(null, now), null);
  assert.equal(parseRetryAfter("soon", now), null);
});

test("forwardablePath blocks traversal and token endpoints", () => {
  assert.equal(forwardablePath(["sims", "12", "usage"], "GET"), "sims/12/usage");
  assert.equal(forwardablePath(["auth", "events"], "GET"), "auth/events");
  assert.equal(forwardablePath(["auth", "events"], "POST"), null);
  assert.equal(forwardablePath(["auth", "me"], "PUT"), "auth/me");
  assert.equal(forwardablePath(["auth", "me"], "GET"), null);
  assert.equal(forwardablePath(["auth", "me", "delete"], "POST"), "auth/me/delete");
  assert.equal(forwardablePath(["auth", "me", "delete"], "GET"), null);
  assert.equal(forwardablePath(["auth", "mfa", "recovery-codes"], "POST"), "auth/mfa/recovery-codes");
  assert.equal(forwardablePath(["auth", "mfa", "verify"], "POST"), null);
  assert.equal(forwardablePath(["auth", "login"], "POST"), null);
  assert.equal(forwardablePath(["Auth", "change-password"], "POST"), null);
  assert.equal(forwardablePath(["sims", ".."], "GET"), null);
  assert.equal(forwardablePath(["sims", "a/b"], "GET"), null);
  assert.equal(forwardablePath([], "GET"), null);
  assert.equal(forwardablePath(["imports", "sims template"], "GET"), "imports/sims%20template");
});

test("homeFor sends Admin to the admin area", () => {
  assert.equal(homeFor("Admin"), "/admin");
  assert.equal(homeFor("SecurityOfficer"), "/dashboard");
  assert.equal(homeFor("Auditor"), "/dashboard");
});
