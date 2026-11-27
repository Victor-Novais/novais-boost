import { test } from "node:test";
import assert from "node:assert/strict";
import { aggregate, isProven, validateResult } from "../src/logic.js";

const valid = {
  cpu: "AMD Ryzen 5 7600X", gpu: "NVIDIA GeForce RTX 4070", ramGb: 32, ramMts: 6000, windowsBuild: "26200", month: "2026-11",
  tweaks: ["power-plan", "power-throttling-off"], group: "Energia", game: "Counter-Strike 2", verdict: "RealGain",
  averageFpsChange: 3.2, onePercentLowChange: 4.1, ciLowPercent: 1.5, ciHighPercent: 6.8, appVersion: "1.3.0",
};

test("accepts the app payload, one row per tweak", () => {
  const result = validateResult(valid);
  assert.equal(result.ok, true);
  assert.equal(result.rows.length, 2);
});

test("rejects extra fields (nothing personal can sneak in)", () => {
  assert.equal(validateResult({ ...valid, email: "x@y.z" }).ok, false);
  assert.equal(validateResult({ ...valid, machine: "DESKTOP-1" }).ok, false);
});

test("rejects bad values", () => {
  assert.equal(validateResult({ ...valid, tweaks: ["DROP TABLE"] }).ok, false);
  assert.equal(validateResult({ ...valid, ciLowPercent: 9, ciHighPercent: 1 }).ok, false);
  assert.equal(validateResult({ ...valid, month: "2026-11-27" }).ok, false);
});

test("aggregates with a 95% interval and promotes only with 30+ results above zero", () => {
  const rows = Array.from({ length: 40 }, (_, i) => ({ tweak: "power-plan", change: 3 + (i % 5) - 2 }));
  rows.push({ tweak: "rare", change: 1 }, { tweak: "rare", change: 2 });
  const [item] = aggregate(rows);
  assert.equal(item.tweak, "power-plan");
  assert.equal(item.samples, 40);
  assert.ok(item.ciLowPercent > 0 && item.ciLowPercent < item.onePercentLowChange);
  assert.equal(isProven(item), true);
  assert.equal(isProven({ ...item, samples: 29 }), false);
});
