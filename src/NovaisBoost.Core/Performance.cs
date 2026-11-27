using System.Globalization;

namespace NovaisBoost.Core;

// ---------------------------------------------------------------- Frame statistics (F7)

public sealed record FrameStats(int Frames, double DurationSeconds, double AverageFps, double OnePercentLowFps,
    double PointOnePercentLowFps, double AverageFrameTimeMs, double P99FrameTimeMs, int Stutters);

public sealed record BenchmarkRun(string Label, string Game, DateTimeOffset CapturedAt, FrameStats Stats, LatencySample? Latency);

public static class FrameAnalysis
{
    /// <summary>"1% low" is the average FPS of the slowest 1% of frames; a stutter is a frame over 2.5x the median.</summary>
    public static FrameStats Compute(IReadOnlyList<double> frameTimesMs)
    {
        var valid = frameTimesMs.Where(time => time > 0 && double.IsFinite(time)).ToArray();
        if (valid.Length < 10)
            throw new InvalidDataException("Poucos quadros capturados. O jogo estava aberto e em foco?");

        var sorted = valid.OrderBy(time => time).ToArray();
        double SlowestAverage(double fraction) => sorted.Skip(sorted.Length - Math.Max(1, (int)(sorted.Length * fraction))).Average();
        var median = sorted[sorted.Length / 2];
        var total = valid.Sum();
        return new FrameStats(
            valid.Length, total / 1000, 1000 * valid.Length / total,
            1000 / SlowestAverage(0.01), 1000 / SlowestAverage(0.001),
            total / valid.Length, sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.99) - 1)],
            valid.Count(time => time > median * 2.5));
    }

    /// <summary>Reads frame times from a PresentMon CSV (1.x "MsBetweenPresents" or 2.x "FrameTime").</summary>
    public static IReadOnlyList<double> ReadPresentMonCsv(TextReader reader)
    {
        var header = reader.ReadLine()?.Split(',') ?? throw new InvalidDataException("CSV do PresentMon vazio.");
        var column = Array.FindIndex(header, name => name.Trim() is "MsBetweenPresents" or "FrameTime" or "msBetweenPresents");
        if (column < 0)
            throw new InvalidDataException("O CSV do PresentMon não tem a coluna de tempo de quadro.");

        var times = new List<double>();
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            if (fields.Length > column && double.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                times.Add(value);
        }
        return times;
    }

    public static double ChangePercent(double before, double after) => before == 0 ? 0 : (after - before) / before * 100;
}

// ---------------------------------------------------------------- DPC/ISR load (F3)

/// <summary>
/// DPC/ISR load from performance counters: share of CPU time (%), not the latency of each call in microseconds.
/// Per-driver latency in µs comes with the ETW trace (F12); until then the UI must say "Carga de DPC/ISR (%)".
/// </summary>
/// <param name="DpcPercent">Average share of CPU time spent in deferred procedure calls.</param>
/// <param name="TimerResolutionMs">Current global timer resolution.</param>
public sealed record LatencySample(double DpcPercent, double DpcPeakPercent, double InterruptPercent,
    double InterruptPeakPercent, double DpcPerSecond, double InterruptsPerSecond, double TimerResolutionMs);

public static class LatencyRules
{
    public static IReadOnlyList<Finding> Evaluate(LatencySample sample)
    {
        var findings = new List<Finding>();
        if (sample.DpcPeakPercent >= 5 || sample.DpcPercent >= 2)
            findings.Add(new Finding("dpc-high", sample.DpcPeakPercent >= 10 ? Severity.Critical : Severity.Warning,
                $"Carga de DPC alta (% da CPU): média {sample.DpcPercent:0.0}% (pico {sample.DpcPeakPercent:0.0}%)",
                "Algum driver está segurando a CPU em chamadas adiadas. Isso causa travadas curtas e input lag.",
                "Atualize drivers de rede, áudio, chipset e GPU. Desconecte dispositivos USB um a um para achar o culpado."));
        if (sample.InterruptPeakPercent >= 5 || sample.InterruptPercent >= 2)
            findings.Add(new Finding("isr-high", Severity.Warning,
                $"Carga de interrupções (ISR) alta (% da CPU): média {sample.InterruptPercent:0.0}% (pico {sample.InterruptPeakPercent:0.0}%)",
                "Dispositivos estão gerando interrupções demais.", "Ative o modo MSI (nível Competitivo) e atualize os drivers."));
        return findings;
    }
}
