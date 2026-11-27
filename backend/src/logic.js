// Pure logic of the NOVAIS boost results service (F21). No I/O: tested with `node --test`.

const TEXT = /^[\p{L}\p{N} .,()+\-_/]{1,60}$/u;
const ID = /^[a-z0-9]+(-[a-z0-9]+)*$/;
const VERDICTS = new Set(["RealGain", "WithinNoise", "Worse"]);
export const MIN_SAMPLES = 30;

/** Validates a POST /v1/results body. Returns { ok, error, rows } — one row per tested tweak. Unknown fields are rejected. */
export function validateResult(body) {
  const allowed = ["cpu", "gpu", "ramGb", "ramMts", "windowsBuild", "month", "tweaks", "group", "game", "verdict",
    "averageFpsChange", "onePercentLowChange", "ciLowPercent", "ciHighPercent", "appVersion"];
  if (!body || typeof body !== "object" || Array.isArray(body)) return { ok: false, error: "corpo inválido" };
  const extra = Object.keys(body).filter((key) => !allowed.includes(key));
  if (extra.length) return { ok: false, error: `campos não aceitos: ${extra.join(", ")}` };
  const text = (value) => typeof value === "string" && TEXT.test(value);
  const number = (value, min, max) => typeof value === "number" && Number.isFinite(value) && value >= min && value <= max;
  const checks = [
    ["cpu", text(body.cpu)], ["gpu", text(body.gpu)], ["game", text(body.game)], ["group", text(body.group)],
    ["ramGb", Number.isInteger(body.ramGb) && body.ramGb >= 0 && body.ramGb <= 2048],
    ["ramMts", Number.isInteger(body.ramMts) && body.ramMts >= 0 && body.ramMts <= 20000],
    ["windowsBuild", typeof body.windowsBuild === "string" && /^\d{5}$/.test(body.windowsBuild)],
    ["month", typeof body.month === "string" && /^\d{4}-\d{2}$/.test(body.month)],
    ["tweaks", Array.isArray(body.tweaks) && body.tweaks.length > 0 && body.tweaks.length <= 40 && body.tweaks.every((id) => typeof id === "string" && ID.test(id) && id.length <= 60)],
    ["verdict", VERDICTS.has(body.verdict)],
    ["averageFpsChange", number(body.averageFpsChange, -90, 300)],
    ["onePercentLowChange", number(body.onePercentLowChange, -90, 300)],
    ["ciLowPercent", number(body.ciLowPercent, -90, 300)],
    ["ciHighPercent", number(body.ciHighPercent, -90, 300)],
    ["appVersion", typeof body.appVersion === "string" && /^[\w.\-]{1,20}$/.test(body.appVersion)],
  ];
  const failed = checks.filter(([, ok]) => !ok).map(([name]) => name);
  if (failed.length) return { ok: false, error: `campos inválidos: ${failed.join(", ")}` };
  if (body.ciLowPercent > body.ciHighPercent) return { ok: false, error: "intervalo invertido" };
  const rows = [...new Set(body.tweaks)].map((tweak) => [body.cpu, body.gpu, body.ramGb, body.ramMts, body.windowsBuild, body.month, tweak,
    body.group, body.game, body.verdict, body.averageFpsChange, body.onePercentLowChange, body.ciLowPercent, body.ciHighPercent, body.appVersion]);
  return { ok: true, rows };
}

/**
 * Aggregates the 1% low change per tweak: mean and a 95% interval of the mean (normal approximation, mean ± 1.96·s/√n).
 * rows: [{ tweak, change }]. Tweaks with fewer than 3 results are left out (too little to say anything).
 */
export function aggregate(rows) {
  const byTweak = new Map();
  for (const { tweak, change } of rows) {
    if (!byTweak.has(tweak)) byTweak.set(tweak, []);
    byTweak.get(tweak).push(change);
  }
  const result = [];
  for (const [tweak, values] of byTweak) {
    const n = values.length;
    if (n < 3) continue;
    const mean = values.reduce((a, b) => a + b, 0) / n;
    const variance = values.reduce((a, b) => a + (b - mean) ** 2, 0) / (n - 1);
    const margin = 1.96 * Math.sqrt(variance / n);
    const round = (value) => Math.round(value * 100) / 100;
    result.push({ tweak, samples: n, onePercentLowChange: round(mean), ciLowPercent: round(mean - margin), ciHighPercent: round(mean + margin) });
  }
  return result.sort((a, b) => a.tweak.localeCompare(b.tweak));
}

export const isProven = (item) => item.samples >= MIN_SAMPLES && item.ciLowPercent > 0;
