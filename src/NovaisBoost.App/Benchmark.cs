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

/// <summary>Frame time capture through Intel PresentMon (2.x command line) plus a parallel DPC/ISR sample.</summary>
internal static class Benchmark
{
    public const string PresentMonUrl = "https://github.com/GameTechDev/PresentMon/releases";

    public static string? FindPresentMon(string dataDirectory)
    {
        var folders = new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "tools"), Path.Combine(dataDirectory, "tools") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var folder in folders.Where(Directory.Exists))
        {
            var match = Directory.EnumerateFiles(folder, "PresentMon*.exe").OrderByDescending(file => file).FirstOrDefault();
            if (match is not null)
                return match;
        }
        return null;
    }

    public static BenchmarkRun Capture(string presentMon, string processName, string game, string label, int seconds, AppServices app)
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
                     "--terminate_after_timed", "--stop_existing_session" })
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

        using var reader = new StreamReader(csv);
        var stats = FrameAnalysis.Compute(FrameAnalysis.ReadPresentMonCsv(reader));
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

        var run = new BenchmarkRun(label, game, DateTimeOffset.Now, stats, sample);
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
