using System.Globalization;
using System.Net;
using System.Text;

namespace NovaisBoost.Core;

/// <summary>
/// The PresentMon console build bundled in the package (tools\). scripts/build.ps1 downloads exactly this file and checks
/// this hash; the app checks it again before running it. A test keeps both in sync.
/// </summary>
public static class PresentMonTool
{
    public const string Version = "2.6.0";
    public const string FileName = "PresentMon-" + Version + "-x64.exe";
    public const string Sha256 = "b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af";
    public const string Url = "https://github.com/GameTechDev/PresentMon/releases/download/v" + Version + "/" + FileName;
}

/// <param name="Hardware">Short lines such as "CPU: AMD Ryzen 5 7600X".</param>
/// <param name="Tweaks">Names of the tweaks applied between "before" and "after".</param>
public sealed record BenchmarkReportInput(string AppVersion, DateTimeOffset CreatedAt, string Game, string Group,
    IReadOnlyList<string> Hardware, IReadOnlyList<string> Tweaks, SessionResult Result,
    IReadOnlyList<FrameStats> Before, IReadOnlyList<FrameStats> After, double[]? TimelineBefore, double[]? TimelineAfter);

/// <summary>Self-contained HTML report (inline CSS and SVG, no scripts, no internet) to share a before × after test.</summary>
public static class BenchmarkReport
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static string FileName(DateTimeOffset createdAt) =>
        $"benchmark-{createdAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.html";

    public static string Html(BenchmarkReportInput input)
    {
        static string E(string text) => WebUtility.HtmlEncode(text);
        string N(double value, string format) => value.ToString(format, PtBr);
        var verdict = input.Result.Overall;
        var verdictClass = verdict switch { Verdict.RealGain => "gain", Verdict.Worse => "worse", _ => "noise" };

        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>NOVAIS boost · {{E(input.Game)}}</title>
            <style>
            :root { --bg: #0f0a1a; --card: #1a1229; --text: #f5f3ff; --muted: #a39cb8; --purple: #a855f7;
                    --gain: #22c55e; --worse: #ef4444; --noise: #eab308; --line: #2e2345; }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--bg); color: var(--text); font: 15px/1.5 "Segoe UI", system-ui, sans-serif; }
            main { max-width: 920px; margin: 0 auto; padding: 24px 16px 48px; }
            .brand { font-size: 34px; font-weight: 900; letter-spacing: 2px; }
            .brand .n { background: linear-gradient(90deg, #7c3aed, #a855f7, #c084fc); -webkit-background-clip: text;
                        background-clip: text; color: transparent; }
            .brand .b { color: #fff; }
            .muted { color: var(--muted); }
            .card { background: var(--card); border: 1px solid var(--line); border-radius: 12px; padding: 16px 20px; margin-top: 16px; }
            .verdict { font-size: 26px; font-weight: 800; }
            .gain { color: var(--gain); } .worse { color: var(--worse); } .noise { color: var(--noise); }
            table { width: 100%; border-collapse: collapse; }
            th, td { text-align: right; padding: 8px 6px; border-bottom: 1px solid var(--line); white-space: nowrap; }
            th:first-child, td:first-child { text-align: left; }
            .table { overflow-x: auto; }
            ul { margin: 6px 0 0; padding-left: 20px; }
            svg { width: 100%; height: auto; display: block; }
            .legend span { display: inline-block; width: 12px; height: 3px; margin: 0 6px 3px 12px; vertical-align: middle; }
            </style>
            </head>
            <body>
            <main>
            <div class="brand"><span class="n">NOVAIS</span> <span class="b">boost</span></div>
            <div class="muted">Relatório de benchmark · {{E(input.CreatedAt.ToString("dd/MM/yyyy HH:mm", PtBr))}} · v{{E(input.AppVersion)}}</div>

            <section class="card">
            <div class="muted">{{E(input.Game)}} · grupo de ajustes: {{E(input.Group)}}</div>
            <div class="verdict {{verdictClass}}">{{E(BenchmarkSession.Describe(verdict))}}</div>
            <div class="muted">{{input.Before.Count}} rodada(s) antes e {{input.After.Count}} depois, primeiros {{N(BenchmarkSession.WarmupSeconds, "0")}} s
            de cada rodada descartados. Intervalo de confiança de 95% por bootstrap ({{N(BenchmarkSession.Resamples, "N0")}} reamostragens).</div>
            </section>

            <section class="card table">
            <table>
            <tr><th>Métrica</th><th>Antes</th><th>Depois</th><th>Diferença</th><th>IC 95%</th><th>Veredito</th></tr>

            """);
        foreach (var metric in input.Result.Metrics)
        {
            var format = metric.Metric == BenchmarkMetric.P99FrameTimeMs ? "0.0" : "0";
            var css = metric.Verdict switch { Verdict.RealGain => "gain", Verdict.Worse => "worse", _ => "noise" };
            html.Append($"<tr><td>{E(BenchmarkSession.Name(metric.Metric))}</td>" +
                $"<td>{N(metric.Before.Mean, format)} ± {N(metric.Before.StdDev, format)}</td>" +
                $"<td>{N(metric.After.Mean, format)} ± {N(metric.After.StdDev, format)}</td>" +
                $"<td>{N(metric.ChangePercent, "+0.0;-0.0;0.0")}%</td>" +
                $"<td>{N(metric.CiLowPercent, "+0.0;-0.0;0.0")}% a {N(metric.CiHighPercent, "+0.0;-0.0;0.0")}%</td>" +
                $"<td class=\"{css}\">{E(BenchmarkSession.Describe(metric.Verdict))}</td></tr>\n");
        }
        html.Append("</table>\n</section>\n");

        if (Chart(input.TimelineBefore, input.TimelineAfter) is { } chart)
            html.Append("<section class=\"card\">\n<div>Tempo de quadro (ms), quanto mais baixo e mais reto, melhor" +
                "<span class=\"legend\"><span style=\"background:#a39cb8\"></span>antes<span style=\"background:#a855f7\"></span>depois</span></div>\n")
                .Append(chart).Append("</section>\n");

        var bottleneck = input.After.Concat(input.Before).Select(stats => stats.Bottleneck).FirstOrDefault(item => item != Bottleneck.Unknown);
        html.Append("<section class=\"card\">\n<div><b>Hardware</b></div>\n<ul>\n");
        foreach (var line in input.Hardware)
            html.Append($"<li>{E(line)}</li>\n");
        if (bottleneck != Bottleneck.Unknown)
            html.Append($"<li>Gargalo no jogo: {E(Describe(bottleneck))}</li>\n");
        html.Append("</ul>\n<div style=\"margin-top:12px\"><b>Ajustes aplicados entre as medições</b></div>\n<ul>\n");
        foreach (var tweak in input.Tweaks)
            html.Append($"<li>{E(tweak)}</li>\n");
        if (input.Tweaks.Count == 0)
            html.Append("<li class=\"muted\">nenhum</li>\n");
        html.Append("</ul>\n</section>\n</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    public static string Describe(Bottleneck bottleneck) => bottleneck switch
    {
        Bottleneck.Gpu => "placa de vídeo (ajustes de CPU e de sistema ajudam pouco)",
        Bottleneck.CpuOrSystem => "processador ou sistema (ajustes de CPU, energia e latência podem ajudar)",
        Bottleneck.FrameLimit => "limite de FPS ou V-Sync (a GPU e a CPU estão sobrando)",
        _ => "não identificado"
    };

    /// <summary>Inline SVG with the before and after frame time lines on the same scale.</summary>
    public static string? Chart(double[]? before, double[]? after)
    {
        var series = new[] { (Values: before, Color: "#a39cb8"), (Values: after, Color: "#a855f7") }
            .Where(item => item.Values is { Length: > 1 }).ToArray();
        if (series.Length == 0)
            return null;
        const double width = 800, height = 220, pad = 24;
        var all = series.SelectMany(item => item.Values!).Where(double.IsFinite).Order().ToArray();
        // Scale to the 99th percentile so one huge hitch does not flatten the rest of the line.
        var top = Math.Max(1, all[Math.Min(all.Length - 1, (int)(all.Length * 0.99))] * 1.2);

        var svg = new StringBuilder($"<svg viewBox=\"0 0 {width} {height}\" role=\"img\" aria-label=\"Tempo de quadro antes e depois\">\n");
        for (var step = 0; step <= 4; step++)
        {
            var y = pad + (height - 2 * pad) * step / 4;
            var label = (top * (4 - step) / 4).ToString("0.#", PtBr);
            svg.Append(string.Create(CultureInfo.InvariantCulture,
                $"<line x1=\"40\" x2=\"{width}\" y1=\"{y:0.#}\" y2=\"{y:0.#}\" stroke=\"#2e2345\"/><text x=\"0\" y=\"{y + 4:0.#}\" fill=\"#a39cb8\" font-size=\"12\">"))
                .Append(label).Append("</text>\n");
        }
        foreach (var (values, color) in series)
        {
            var points = values!.Select((value, index) =>
            {
                var x = 40 + (width - 40) * index / (values!.Length - 1);
                var y = pad + (height - 2 * pad) * (1 - Math.Min(value, top) / top);
                return string.Create(CultureInfo.InvariantCulture, $"{x:0.#},{y:0.#}");
            });
            svg.Append($"<polyline fill=\"none\" stroke=\"{color}\" stroke-width=\"1.5\" points=\"{string.Join(' ', points)}\"/>\n");
        }
        return svg.Append("</svg>\n").ToString();
    }
}
