using System.Globalization;

namespace NovaisBoost.Core;

// ---------------------------------------------------------------- Frame statistics (F7, F12)

/// <summary>What limits the frame rate, from PresentMon's per-frame busy times.</summary>
/// <remarks>Gpu: the GPU is busy almost the whole frame. CpuOrSystem: the GPU waits for the CPU, the driver or the OS.
/// FrameLimit: both are mostly idle (FPS cap, V-Sync). Unknown: the capture has no GPU busy data.</remarks>
public enum Bottleneck { Unknown, Gpu, CpuOrSystem, FrameLimit }

/// <summary>One frame from PresentMon. Busy and latency values are null when the column is missing or "NA".</summary>
public sealed record FrameSample(double FrameTimeMs, double? CpuBusyMs = null, double? GpuBusyMs = null,
    double? PcLatencyMs = null, double? ClickToPhotonMs = null);

/// <param name="GpuBusyPercent">Median GPU busy time as a share of the median frame time.</param>
/// <param name="AveragePcLatencyMs">PC latency (input to display), only reported by games with NVIDIA Reflex or similar markers.</param>
public sealed record FrameStats(int Frames, double DurationSeconds, double AverageFps, double OnePercentLowFps,
    double PointOnePercentLowFps, double AverageFrameTimeMs, double P99FrameTimeMs, int Stutters,
    Bottleneck Bottleneck = Bottleneck.Unknown, double? GpuBusyPercent = null, double? CpuBusyPercent = null,
    double? AveragePcLatencyMs = null, double? AverageClickToPhotonMs = null);

/// <param name="Timeline">Frame times downsampled for charts (slowest frame of each bucket, so spikes stay visible).</param>
public sealed record BenchmarkRun(string Label, string Game, DateTimeOffset CapturedAt, FrameStats Stats, LatencySample? Latency,
    double[]? Timeline = null);

public static class FrameAnalysis
{
    /// <summary>A stutter is a frame slower than this multiple of the median frame time.</summary>
    public const double StutterFactor = 2;

    /// <summary>GPU busy for at least this share of the frame time means the game is limited by the GPU.</summary>
    public const double GpuBoundShare = 0.90;

    /// <summary>CPU and GPU both busy for less than this share of the frame time means an FPS cap or V-Sync.</summary>
    public const double IdleShare = 0.75;

    private static readonly string[] FrameTimeColumns = ["MsBetweenPresents", "FrameTime", "msBetweenPresents"];
    private static readonly string[] CpuBusyColumns = ["MsCPUBusy", "CPUBusy"];
    private static readonly string[] GpuBusyColumns = ["MsGPUBusy", "GPUBusy", "msGPUActive"];
    private static readonly string[] PcLatencyColumns = ["MsPCLatency", "PCLatency"];
    private static readonly string[] ClickToPhotonColumns = ["MsClickToPhotonLatency", "ClickToPhotonLatency"];

    /// <summary>"1% low" is the average FPS of the slowest 1% of frames.</summary>
    public static FrameStats Compute(IReadOnlyList<double> frameTimesMs) =>
        Compute(frameTimesMs.Select(time => new FrameSample(time)).ToArray());

    public static FrameStats Compute(IReadOnlyList<FrameSample> frames)
    {
        var validFrames = frames.Where(frame => frame.FrameTimeMs > 0 && double.IsFinite(frame.FrameTimeMs)).ToArray();
        if (validFrames.Length < 10)
            throw new InvalidDataException("Poucos quadros capturados. O jogo estava aberto e em foco?");

        var valid = validFrames.Select(frame => frame.FrameTimeMs).ToArray();
        var sorted = valid.Order().ToArray();
        double SlowestAverage(double fraction) => sorted.Skip(sorted.Length - Math.Max(1, (int)(sorted.Length * fraction))).Average();
        var median = Median(sorted);
        var total = valid.Sum();

        var gpuShare = Share(validFrames, frame => frame.GpuBusyMs, median);
        var cpuShare = Share(validFrames, frame => frame.CpuBusyMs, median);
        var bottleneck = gpuShare switch
        {
            null => Bottleneck.Unknown,
            >= GpuBoundShare => Bottleneck.Gpu,
            < IdleShare when cpuShare < IdleShare => Bottleneck.FrameLimit,
            _ => Bottleneck.CpuOrSystem
        };

        return new FrameStats(
            valid.Length, total / 1000, 1000 * valid.Length / total,
            1000 / SlowestAverage(0.01), 1000 / SlowestAverage(0.001),
            total / valid.Length, sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.99) - 1)],
            valid.Count(time => time > median * StutterFactor),
            bottleneck, gpuShare * 100, cpuShare * 100,
            AverageOf(validFrames, frame => frame.PcLatencyMs), AverageOf(validFrames, frame => frame.ClickToPhotonMs));
    }

    /// <summary>Drops the first seconds of a capture (loading, shader compilation, camera moving into place).</summary>
    public static IReadOnlyList<FrameSample> SkipWarmup(IReadOnlyList<FrameSample> frames, double seconds)
    {
        var elapsed = 0d;
        var index = 0;
        while (index < frames.Count && elapsed < seconds * 1000)
            elapsed += Math.Max(0, frames[index++].FrameTimeMs);
        return frames.Skip(index).ToArray();
    }

    /// <summary>Reads frame times from a PresentMon CSV (1.x "MsBetweenPresents" or 2.x "FrameTime").</summary>
    public static IReadOnlyList<double> ReadPresentMonCsv(TextReader reader) =>
        ReadPresentMonFrames(reader).Select(frame => frame.FrameTimeMs).ToArray();

    /// <summary>
    /// Reads per-frame data from a PresentMon CSV. Accepts 2.6 names (MsBetweenPresents, MsCPUBusy, MsGPUBusy,
    /// MsPCLatency, MsClickToPhotonLatency), older 2.x names (FrameTime, CPUBusy, GPUBusy...) and 1.x (msGPUActive).
    /// </summary>
    public static IReadOnlyList<FrameSample> ReadPresentMonFrames(TextReader reader)
    {
        var header = reader.ReadLine()?.Split(',').Select(name => name.Trim()).ToArray()
            ?? throw new InvalidDataException("CSV do PresentMon vazio.");
        int Find(string[] names) => Array.FindIndex(header, name => names.Contains(name, StringComparer.Ordinal));
        var frameColumn = Find(FrameTimeColumns);
        if (frameColumn < 0)
            throw new InvalidDataException("O CSV do PresentMon não tem a coluna de tempo de quadro.");
        var (cpu, gpu, pcLatency, click) = (Find(CpuBusyColumns), Find(GpuBusyColumns), Find(PcLatencyColumns), Find(ClickToPhotonColumns));

        var frames = new List<FrameSample>();
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split(',');
            double? Value(int column) => column >= 0 && column < fields.Length &&
                double.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
                    ? value : null;
            if (Value(frameColumn) is { } frameTime)
                frames.Add(new FrameSample(frameTime, Value(cpu), Value(gpu), Value(pcLatency), Value(click)));
        }
        return frames;
    }

    /// <summary>Reduces frame times to at most <paramref name="points"/> values, keeping the slowest frame of each bucket.</summary>
    public static double[] Downsample(IReadOnlyList<double> frameTimesMs, int points)
    {
        if (points <= 0)
            throw new ArgumentOutOfRangeException(nameof(points));
        if (frameTimesMs.Count <= points)
            return frameTimesMs.ToArray();
        var result = new double[points];
        for (var bucket = 0; bucket < points; bucket++)
        {
            var start = (int)((long)bucket * frameTimesMs.Count / points);
            var end = (int)((long)(bucket + 1) * frameTimesMs.Count / points);
            var slowest = 0d;
            for (var index = start; index < end; index++)
                slowest = Math.Max(slowest, frameTimesMs[index]);
            result[bucket] = slowest;
        }
        return result;
    }

    /// <summary>Text chart of frame times (taller bar = slower frame), for the terminal.</summary>
    public static string Sparkline(IReadOnlyList<double> frameTimesMs, int width)
    {
        const string bars = "▁▂▃▄▅▆▇█";
        var values = Downsample(frameTimesMs, width);
        if (values.Length == 0)
            return "";
        var (min, max) = (values.Min(), values.Max());
        return string.Concat(values.Select(value =>
            bars[max - min < 1e-9 ? 0 : (int)Math.Round((value - min) / (max - min) * (bars.Length - 1))]));
    }

    public static double ChangePercent(double before, double after) => before == 0 ? 0 : (after - before) / before * 100;

    internal static double Median(IReadOnlyList<double> sorted) => sorted.Count % 2 == 1
        ? sorted[sorted.Count / 2]
        : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;

    private static double? Share(FrameSample[] frames, Func<FrameSample, double?> busy, double medianFrameTime)
    {
        var values = frames.Select(busy).OfType<double>().Where(value => value >= 0).Order().ToArray();
        return values.Length < 10 || medianFrameTime <= 0 ? null : Median(values) / medianFrameTime;
    }

    private static double? AverageOf(FrameSample[] frames, Func<FrameSample, double?> value)
    {
        var values = frames.Select(value).OfType<double>().Where(item => item > 0).ToArray();
        return values.Length == 0 ? null : values.Average();
    }
}

// ---------------------------------------------------------------- Statistical session (F12)

public enum BenchmarkMetric { AverageFps, OnePercentLowFps, PointOnePercentLowFps, P99FrameTimeMs }

/// <summary>RealGain: the 95% interval is entirely on the "better" side. Worse: entirely on the "worse" side. Otherwise noise.</summary>
public enum Verdict { RealGain, WithinNoise, Worse }

public sealed record MetricSummary(double Mean, double StdDev, int Count);

/// <param name="ChangePercent">Change of the mean (after vs. before), in %. Positive = higher value after.</param>
/// <param name="CiLowPercent">Lower bound of the 95% bootstrap confidence interval of the change, in %.</param>
/// <param name="EffectSize">Cohen's d (difference of means over the pooled standard deviation); null when both sides have no spread.</param>
public sealed record MetricComparison(BenchmarkMetric Metric, MetricSummary Before, MetricSummary After,
    double ChangePercent, double CiLowPercent, double CiHighPercent, double? EffectSize, Verdict Verdict);

public sealed record SessionResult(IReadOnlyList<MetricComparison> Metrics, Verdict Overall)
{
    public MetricComparison this[BenchmarkMetric metric] => Metrics.Single(item => item.Metric == metric);
}

/// <summary>
/// Before × after comparison over several benchmark rounds. Each round is one capture (warm-up already removed);
/// the bootstrap resamples whole rounds, because frames inside one round are not independent.
/// </summary>
public static class BenchmarkSession
{
    public const int MinRounds = 3;
    public const int MaxRounds = 5;
    public const double WarmupSeconds = 10;
    public const int Resamples = 10_000;

    /// <summary>A change smaller than this (in %) is never called a gain or a loss, even when consistent.</summary>
    public const double MinimumEffectPercent = 1;

    public static SessionResult Evaluate(IReadOnlyList<FrameStats> before, IReadOnlyList<FrameStats> after, int? seed = null)
    {
        foreach (var (side, rounds) in new[] { ("antes", before), ("depois", after) })
            if (rounds.Count is < MinRounds or > MaxRounds)
                throw new ArgumentException($"O lado \"{side}\" precisa de {MinRounds} a {MaxRounds} rodadas (tem {rounds.Count}).");

        var random = seed is { } value ? new Random(value) : new Random();
        var metrics = Enum.GetValues<BenchmarkMetric>()
            .Select(metric => Compare(metric, before.Select(stats => Read(stats, metric)).ToArray(),
                after.Select(stats => Read(stats, metric)).ToArray(), random))
            .ToArray();

        // The verdict uses the two metrics players feel: average FPS and 1% low. Any proven loss wins over a gain.
        var key = metrics.Where(item => item.Metric is BenchmarkMetric.AverageFps or BenchmarkMetric.OnePercentLowFps).ToArray();
        var overall = key.Any(item => item.Verdict == Verdict.Worse) ? Verdict.Worse
            : key.Any(item => item.Verdict == Verdict.RealGain) ? Verdict.RealGain
            : Verdict.WithinNoise;
        return new SessionResult(metrics, overall);
    }

    public static MetricComparison Compare(BenchmarkMetric metric, IReadOnlyList<double> before, IReadOnlyList<double> after, Random random)
    {
        if (before.Count == 0 || after.Count == 0)
            throw new ArgumentException("Cada lado precisa de pelo menos uma rodada.");
        var (beforeSummary, afterSummary) = (Summarize(before), Summarize(after));
        var change = FrameAnalysis.ChangePercent(beforeSummary.Mean, afterSummary.Mean);

        var changes = new double[Resamples];
        for (var sample = 0; sample < Resamples; sample++)
            changes[sample] = FrameAnalysis.ChangePercent(ResampledMean(before, random), ResampledMean(after, random));
        Array.Sort(changes);
        var (low, high) = (Percentile(changes, 0.025), Percentile(changes, 0.975));

        var higherIsBetter = HigherIsBetter(metric);
        var better = higherIsBetter ? low > 0 : high < 0;
        var worse = higherIsBetter ? high < 0 : low > 0;
        var meaningful = Math.Abs(change) >= MinimumEffectPercent;
        var verdict = meaningful && better ? Verdict.RealGain : meaningful && worse ? Verdict.Worse : Verdict.WithinNoise;

        var pooled = Math.Sqrt((Math.Pow(beforeSummary.StdDev, 2) + Math.Pow(afterSummary.StdDev, 2)) / 2);
        double? effect = pooled > 1e-12 ? (afterSummary.Mean - beforeSummary.Mean) / pooled : null;
        return new MetricComparison(metric, beforeSummary, afterSummary, change, low, high, effect, verdict);
    }

    public static bool HigherIsBetter(BenchmarkMetric metric) => metric != BenchmarkMetric.P99FrameTimeMs;

    public static string Name(BenchmarkMetric metric) => metric switch
    {
        BenchmarkMetric.AverageFps => "FPS médio",
        BenchmarkMetric.OnePercentLowFps => "1% low",
        BenchmarkMetric.PointOnePercentLowFps => "0,1% low",
        _ => "Frametime p99 (ms)"
    };

    public static string Describe(Verdict verdict) => verdict switch
    {
        Verdict.RealGain => "Ganho real",
        Verdict.Worse => "Piorou",
        _ => "Dentro do ruído"
    };

    public static double Read(FrameStats stats, BenchmarkMetric metric) => metric switch
    {
        BenchmarkMetric.AverageFps => stats.AverageFps,
        BenchmarkMetric.OnePercentLowFps => stats.OnePercentLowFps,
        BenchmarkMetric.PointOnePercentLowFps => stats.PointOnePercentLowFps,
        _ => stats.P99FrameTimeMs
    };

    public static MetricSummary Summarize(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var variance = values.Count < 2 ? 0 : values.Sum(value => Math.Pow(value - mean, 2)) / (values.Count - 1);
        return new MetricSummary(mean, Math.Sqrt(variance), values.Count);
    }

    private static double ResampledMean(IReadOnlyList<double> values, Random random)
    {
        var sum = 0d;
        for (var index = 0; index < values.Count; index++)
            sum += values[random.Next(values.Count)];
        return sum / values.Count;
    }

    private static double Percentile(double[] sorted, double fraction)
    {
        var position = fraction * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(sorted.Length - 1, lower + 1);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }
}

// ---------------------------------------------------------------- DPC/ISR load (F3)

/// <summary>
/// DPC/ISR load from performance counters: share of CPU time (%), not the latency of each call.
/// Per-driver latency in µs comes from the ETW trace (<see cref="KernelLatencyAnalysis"/>).
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
