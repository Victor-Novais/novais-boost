using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using NovaisBoost.Core;

namespace NovaisBoost.App;

internal static class ReportStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string SaveDiagnostic(AppServices app, HardwareSnapshot pc, DiagnosticReport report)
    {
        var directory = Path.Combine(app.DataDirectory, "reports");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"diagnostico-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { pc, report }, Json));
        app.Log.Info($"Relatório de diagnóstico salvo em {path}.");
        return path;
    }
}

/// <summary>Frame time capture through Intel PresentMon (2.x command line) plus a parallel DPC/ISR load sample.</summary>
internal static class Benchmark
{
    /// <summary>
    /// The PresentMon bundled next to the exe (tools\, verified against the pinned SHA256), or one the administrator put
    /// in the data folder. The PATH and the exe folder itself are not searched: PresentMon runs elevated.
    /// </summary>
    public static string? FindPresentMon(string dataDirectory, IActionLog log)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", PresentMonTool.FileName);
        if (File.Exists(bundled))
        {
            using var stream = File.OpenRead(bundled);
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
            if (hash == PresentMonTool.Sha256)
                return bundled;
            log.Warning($"PresentMon embutido ignorado: SHA256 {hash} diferente do esperado.");
        }
        var folder = Path.Combine(dataDirectory, "tools");
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "PresentMon*.exe").OrderByDescending(file => file, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
            : null;
    }

    /// <param name="warmupSeconds">Seconds discarded at the start (0 for a single capture, 10 in an A/B session).</param>
    public static BenchmarkRun Capture(string presentMon, string processName, string game, string label, int seconds, AppServices app,
        double warmupSeconds = 0)
    {
        var folder = Path.Combine(app.DataDirectory, "benchmarks");
        Directory.CreateDirectory(folder);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var csv = Path.Combine(folder, $"{stamp}-{label}.csv");

        using var cancel = new CancellationTokenSource();
        var latency = Task.Run(() => LatencyProbe.Measure(TimeSpan.FromSeconds(Math.Min(seconds, 30)), cancel.Token));
        var info = new ProcessStartInfo(presentMon)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "--process_name", processName, "--output_file", csv, "--timed", seconds.ToString(),
                     "--terminate_after_timed", "--stop_existing_session", "--no_console_stats" })
            info.ArgumentList.Add(argument);

        using (var process = Process.Start(info) ?? throw new InvalidOperationException("Falha ao iniciar o PresentMon."))
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(seconds + 30)))
            {
                process.Kill();
                throw new TimeoutException("O PresentMon não terminou no tempo esperado.");
            }
            if (!File.Exists(csv))
                throw new InvalidOperationException($"O PresentMon não gerou dados. O jogo ({processName}) estava aberto? {error.Result}{output.Result}".Trim());
        }

        IReadOnlyList<FrameSample> frames;
        using (var reader = new StreamReader(csv))
            frames = FrameAnalysis.SkipWarmup(FrameAnalysis.ReadPresentMonFrames(reader), warmupSeconds);
        var stats = FrameAnalysis.Compute(frames);
        LatencySample? sample = null;
        try
        {
            sample = latency.Wait(TimeSpan.FromSeconds(10)) ? latency.Result : null;
        }
        catch (AggregateException)
        {
            // Latency is a bonus; frame data is the result.
        }
        cancel.Cancel();

        var run = new BenchmarkRun(label, game, DateTimeOffset.Now, stats, sample,
            FrameAnalysis.Downsample(frames.Select(frame => frame.FrameTimeMs).ToArray(), 600));
        File.WriteAllText(Path.Combine(folder, $"{stamp}-{label}.json"), JsonSerializer.Serialize(run, ReportStore.Json));
        app.Log.Info($"Benchmark {label} de {game}: {stats.AverageFps:0} FPS, 1% low {stats.OnePercentLowFps:0}.");
        return run;
    }

    public static IReadOnlyList<BenchmarkRun> History(string dataDirectory)
    {
        var folder = Path.Combine(dataDirectory, "benchmarks");
        if (!Directory.Exists(folder))
            return [];
        return Directory.EnumerateFiles(folder, "*.json").Select(file =>
            {
                try
                {
                    return JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(file), ReportStore.Json);
                }
                catch (JsonException)
                {
                    return null;
                }
            })
            .OfType<BenchmarkRun>().OrderBy(run => run.CapturedAt).ToArray();
    }
}
