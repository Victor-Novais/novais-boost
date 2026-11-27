// Cloudflare Worker: POST /v1/results, GET /v1/evidence?cpu=&gpu=. Data in D1 (binding DB). The client IP is never stored.
import { aggregate, validateResult } from "./logic.js";

const JSON_HEADERS = { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" };
const json = (status, body) => new Response(JSON.stringify(body), { status, headers: JSON_HEADERS });

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    if (request.method === "POST" && url.pathname === "/v1/results") {
      if (Number(request.headers.get("content-length") ?? 0) > 4096) return json(413, { error: "corpo grande demais" });
      let body;
      try {
        body = await request.json();
      } catch {
        return json(400, { error: "JSON inválido" });
      }
      const check = validateResult(body);
      if (!check.ok) return json(400, { error: check.error });
      const insert = env.DB.prepare(
        "INSERT INTO results (cpu, gpu, ram_gb, ram_mts, windows_build, month, tweak, grp, game, verdict, " +
        "average_fps_change, one_percent_low_change, ci_low, ci_high, app_version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
      await env.DB.batch(check.rows.map((row) => insert.bind(...row)));
      return json(201, { stored: check.rows.length });
    }
    if (request.method === "GET" && url.pathname === "/v1/evidence") {
      const cpu = url.searchParams.get("cpu") ?? "";
      const gpu = url.searchParams.get("gpu") ?? "";
      if (!cpu || !gpu || cpu.length > 60 || gpu.length > 60) return json(400, { error: "informe cpu e gpu" });
      const { results } = await env.DB.prepare(
        "SELECT tweak, one_percent_low_change AS change FROM results WHERE cpu = ? AND gpu = ? LIMIT 50000").bind(cpu, gpu).all();
      return new Response(JSON.stringify(aggregate(results)), { status: 200, headers: { ...JSON_HEADERS, "cache-control": "public, max-age=3600" } });
    }
    return json(404, { error: "não encontrado" });
  },
};
