// Run with `npm test`.
import { test } from "node:test";
import assert from "node:assert/strict";
import { OUTCOME_FILTERS, alertOutcome, auditActionLabel, outcomeQuery } from "./alert-outcome.js";
import { activityText } from "../components/sims/activity-text.js";

test("a flagged alert shows as flagged and an escalated one as escalated", () => {
  assert.equal(alertOutcome({ status: "flagged", resolution: null }), "flagged");
  assert.equal(alertOutcome({ status: "escalated", resolution: null }), "escalated");
});

test("a cleared alert with confirmed misuse shows as confirmed", () => {
  assert.equal(alertOutcome({ status: "cleared", resolution: "confirmedMisuse" }), "confirmed");
});

test("a cleared alert dismissed as a false positive shows as cleared", () => {
  assert.equal(alertOutcome({ status: "cleared", resolution: "falsePositive" }), "cleared");
});

test("a cleared alert with no resolution falls back to cleared", () => {
  assert.equal(alertOutcome({ status: "cleared", resolution: null }), "cleared");
  assert.equal(alertOutcome({ status: "cleared" }), "cleared");
});

test("the filter options are flagged, escalated, confirmed and cleared", () => {
  assert.deepEqual(OUTCOME_FILTERS, ["flagged", "escalated", "confirmed", "cleared"]);
});

test("confirmed and cleared both ask for cleared alerts with their own resolution", () => {
  assert.deepEqual(outcomeQuery("confirmed"), { status: "cleared", resolution: "confirmedMisuse" });
  assert.deepEqual(outcomeQuery("cleared"), { status: "cleared", resolution: "falsePositive" });
  assert.deepEqual(outcomeQuery("flagged"), { status: "flagged" });
  assert.deepEqual(outcomeQuery("escalated"), { status: "escalated" });
  assert.deepEqual(outcomeQuery(""), {});
  assert.deepEqual(outcomeQuery(undefined), {});
});

test("the audit log says confirmed as misuse when misuse was confirmed", () => {
  const row = {
    action: "alert.status_changed",
    oldValues: { status: "flagged", resolution: null },
    newValues: { status: "cleared", resolution: "confirmedMisuse" },
  };
  assert.equal(auditActionLabel(row), "Alert confirmed as misuse");
});

test("the audit log keeps Alert cleared for a false positive", () => {
  const row = {
    action: "alert.status_changed",
    oldValues: { status: "flagged", resolution: null },
    newValues: { status: "cleared", resolution: "falsePositive" },
  };
  assert.equal(auditActionLabel(row), "Alert cleared");
});

test("the audit log still says reopened and shows other actions as before", () => {
  const reopened = {
    action: "alert.status_changed",
    oldValues: { status: "cleared", resolution: "confirmedMisuse" },
    newValues: { status: "escalated", resolution: null },
  };
  assert.equal(auditActionLabel(reopened), "Alert reopened");
  assert.equal(auditActionLabel({ action: "sim.created" }), "SIM added");
  assert.equal(auditActionLabel({ action: "something.new" }), "something.new");
});

test("the SIM timeline says confirmed as misuse only for a confirmed alert", () => {
  assert.equal(activityText({ kind: "alertCleared", resolution: "confirmedMisuse" }), "Alert confirmed as misuse");
  assert.equal(activityText({ kind: "alertCleared", resolution: "falsePositive" }), "Alert cleared");
  assert.equal(activityText({ kind: "alertCleared", resolution: null }), "Alert cleared");
  assert.equal(activityText({ kind: "alertOpened" }), "Alert opened");
});
