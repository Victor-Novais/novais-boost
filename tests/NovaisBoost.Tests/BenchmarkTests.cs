using System.Text.Json;
using System.Text.Json.Serialization;
using NovaisBoost.Core;

namespace NovaisBoost.Tests;

public sealed class BenchmarkTests
{
    private const int Seed = 1234;

    // ------------------------------------------------------------------ PresentMon CSV and per-frame metrics

    [Fact]
    public void ReadsPresentMon26Columns()
    {
        const string csv = """
            Application,ProcessID,PresentMode,CPUStartTime,MsCPUBusy,MsCPUWait,MsGPULatency,MsGPUTime,MsGPUBusy,MsGPUWait,MsClickToPhotonLatency,MsBetweenPresents,MsPCLatency
            cs2.exe,100,Hardware: Independent Flip,1.0,2.0,0.5,0.1,3.8,3.9,0.0,NA,4.0,12.5
            cs2.exe,100,Hardware: Independent Flip,5.0,2.1,0.4,0.1,3.7,3.8,0.1,18.0,4.1,NA
            cs2.exe,100,Hardware: Independent Flip,9.0,NA,NA,NA,NA,NA,NA,NA,NA,NA
            """;
        var frames = FrameAnalysis.ReadPresentMonFrames(new StringReader(csv));
        Assert.Equal(2, frames.Count);
        Assert.Equal(new FrameSample(4.0, 2.0, 3.9, 12.5, null), frames[0]);
        Assert.Equal(new FrameSample(4.1, 2.1, 3.8, null, 18.0), frames[1]);
    }

    [Fact]
    public void ReadsOlderPresentMon2Columns()
    {
        var frames = FrameAnalysis.ReadPresentMonFrames(new StringReader("Application,FrameTime,CPUBusy,GPUBusy,ClickToPhotonLatency\ncs2.exe,5,1,4.8,20\n"));
        Assert.Equal(new FrameSample(5, 1, 4.8, null, 20), Assert.Single(frames));
    }

    [Fact]
    public void GpuBoundCaptureIsClassifiedAsGpu()
    {
        var frames = Enumerable.Range(0, 100).Select(_ => new FrameSample(6.9, CpuBusyMs: 3, GpuBusyMs: 6.7)).ToArray();
        var stats = FrameAnalysis.Compute(frames);
        Assert.Equal(Bottleneck.Gpu, stats.Bottleneck);
        Assert.InRange(stats.GpuBusyPercent!.Value, 96, 98);
    }

    [Fact]
    public void CpuBoundCaptureIsClassifiedAsCpuOrSystem()
    {
        var frames = Enumerable.Range(0, 100).Select(_ => new FrameSample(6.9, CpuBusyMs: 6.5, GpuBusyMs: 3.1, PcLatencyMs: 15)).ToArray();
        var stats = FrameAnalysis.Compute(frames);
        Assert.Equal(Bottleneck.CpuOrSystem, stats.Bottleneck);
        Assert.Equal(15, stats.AveragePcLatencyMs);
    }

    [Fact]
    public void CappedCaptureIsClassifiedAsFrameLimit()
    {
        var frames = Enumerable.Range(0, 100).Select(_ => new FrameSample(6.94, CpuBusyMs: 2, GpuBusyMs: 3)).ToArray();
        Assert.Equal(Bottleneck.FrameLimit, FrameAnalysis.Compute(frames).Bottleneck);
    }

    [Fact]
    public void CaptureWithoutGpuDataHasUnknownBottleneck()
    {
        var stats = FrameAnalysis.Compute(Enumerable.Repeat(5.0, 100).ToArray());
        Assert.Equal(Bottleneck.Unknown, stats.Bottleneck);
        Assert.Null(stats.GpuBusyPercent);
    }

    [Fact]
    public void StutterIsAFrameOverTwiceTheMedian()
    {
        var frames = Enumerable.Repeat(5.0, 990).Concat(Enumerable.Repeat(10.5, 5)).Concat(Enumerable.Repeat(9.9, 5)).ToArray();
        Assert.Equal(5, FrameAnalysis.Compute(frames).Stutters);
    }

    [Fact]
    public void WarmupDropsTheFirstSeconds()
    {
        var frames = Enumerable.Repeat(new FrameSample(100), 200).ToArray(); // 20 s at 10 FPS
        Assert.Equal(100, FrameAnalysis.SkipWarmup(frames, 10).Count);
        Assert.Equal(200, FrameAnalysis.SkipWarmup(frames, 0).Count);
        Assert.Empty(FrameAnalysis.SkipWarmup(frames, 60));
    }

    [Fact]
    public void DownsampleKeepsTheSlowestFrameOfEachBucket()
    {
        var frames = Enumerable.Repeat(5.0, 1000).ToArray();
        frames[503] = 40;
        var points = FrameAnalysis.Downsample(frames, 100);
        Assert.Equal(100, points.Length);
        Assert.Equal(40, points.Max());
        Assert.Equal(1, points.Count(point => point == 40));
        Assert.Equal([1.0, 2.0], FrameAnalysis.Downsample([1, 2], 100));
    }

    [Fact]
    public void SparklineShowsSpikesAsTallBars()
    {
        var line = FrameAnalysis.Sparkline([5, 5, 5, 20, 5, 5], 6);
        Assert.Equal("▁▁▁█▁▁", line);
        Assert.Equal("▁▁▁", FrameAnalysis.Sparkline([5, 5, 5], 10));
    }

    // ------------------------------------------------------------------ Statistical session

    private static FrameStats Round(double fps, double oneLow, double p99 = 10) =>
        new(10_000, 50, fps, oneLow, oneLow * 0.8, 1000 / fps, p99, 0);

    [Fact]
    public void KnownGainIsARealGain()
    {
        FrameStats[] before = [Round(200, 120, 9), Round(202, 121, 9.1), Round(199, 119, 8.9)];
        FrameStats[] after = [Round(220, 140, 7.9), Round(222, 141, 8.0), Round(219, 139, 7.8)];
        var result = BenchmarkSession.Evaluate(before, after, Seed);

        Assert.Equal(Verdict.RealGain, result.Overall);
        var fps = result[BenchmarkMetric.AverageFps];
        Assert.Equal(Verdict.RealGain, fps.Verdict);
        Assert.InRange(fps.ChangePercent, 9.5, 10.5);
        Assert.True(fps.CiLowPercent > 8 && fps.CiHighPercent < 12, $"{fps.CiLowPercent}..{fps.CiHighPercent}");
        Assert.True(fps.EffectSize > 5);
        Assert.Equal(Verdict.RealGain, result[BenchmarkMetric.P99FrameTimeMs].Verdict); // lower frame time is better
        Assert.Equal(3, fps.Before.Count);
    }

    [Fact]
    public void NoiseOnlyIsWithinNoise()
    {
        FrameStats[] before = [Round(200, 120), Round(212, 131), Round(190, 112), Round(205, 126)];
        FrameStats[] after = [Round(208, 125), Round(195, 116), Round(203, 128), Round(199, 119)];
        var result = BenchmarkSession.Evaluate(before, after, Seed);

        Assert.Equal(Verdict.WithinNoise, result.Overall);
        var fps = result[BenchmarkMetric.AverageFps];
        Assert.True(fps.CiLowPercent < 0 && fps.CiHighPercent > 0);
    }

    [Fact]
    public void KnownLossIsWorseEvenWithAGainElsewhere()
    {
        FrameStats[] before = [Round(200, 120), Round(201, 121), Round(199, 119)];
        FrameStats[] after = [Round(210, 100), Round(211, 101), Round(209, 99)]; // more FPS, but much worse 1% low
        var result = BenchmarkSession.Evaluate(before, after, Seed);

        Assert.Equal(Verdict.RealGain, result[BenchmarkMetric.AverageFps].Verdict);
        Assert.Equal(Verdict.Worse, result[BenchmarkMetric.OnePercentLowFps].Verdict);
        Assert.Equal(Verdict.Worse, result.Overall);
    }

    [Fact]
    public void TinyConsistentChangeIsStillNoise()
    {
        FrameStats[] before = [Round(200, 120), Round(200.1, 120.1), Round(199.9, 119.9)];
        FrameStats[] after = [Round(201, 120.6), Round(201.1, 120.7), Round(200.9, 120.5)]; // +0.5%
        Assert.Equal(Verdict.WithinNoise, BenchmarkSession.Evaluate(before, after, Seed).Overall);
    }

    [Fact]
    public void BootstrapIsReproducibleWithASeed()
    {
        FrameStats[] before = [Round(200, 120), Round(212, 131), Round(190, 112)];
        FrameStats[] after = [Round(215, 130), Round(205, 125), Round(222, 139)];
        Assert.Equal(BenchmarkSession.Evaluate(before, after, Seed).Metrics, BenchmarkSession.Evaluate(before, after, Seed).Metrics);
    }

    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 6)]
    public void SessionNeedsThreeToFiveRoundsPerSide(int before, int after) =>
        Assert.Throws<ArgumentException>(() => BenchmarkSession.Evaluate(
            Enumerable.Repeat(Round(200, 120), before).ToArray(), Enumerable.Repeat(Round(200, 120), after).ToArray(), Seed));

    // ------------------------------------------------------------------ Local evidence

    private static LocalEvidenceEntry Entry(string id, Verdict verdict, int day) =>
        new(id, "CPU", "Counter-Strike 2", verdict, 1, 2, 0.5, 3.5, new DateTimeOffset(2026, 11, day, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void LocalGainPromotesAnExperimentalTweakIntoTheLevels()
    {
        var tweaks = TweakCatalog.LoadEmbedded();
        var local = new LocalEvidence([Entry("priority-separation", Verdict.RealGain, 2)]);
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Competitive), tweak => tweak.Id == "priority-separation");
        Assert.Contains(TweakLevels.Select(tweaks, TweakLevel.Competitive, local), tweak => tweak.Id == "priority-separation");
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Safe, local), tweak => tweak.Id == "priority-separation");
        Assert.Equal(TweakEvidence.Measured, local.Effective(tweaks.Single(tweak => tweak.Id == "priority-separation")));
    }

    [Fact]
    public void LocalLossRemovesATweakFromTheLevelsAndTheLatestTestWins()
    {
        var tweaks = TweakCatalog.LoadEmbedded();
        var worse = new LocalEvidence([Entry("game-mode", Verdict.RealGain, 1), Entry("game-mode", Verdict.Worse, 3)]);
        Assert.DoesNotContain(TweakLevels.Select(tweaks, TweakLevel.Extreme, worse), tweak => tweak.Id == "game-mode");

        var retested = worse.With([Entry("game-mode", Verdict.WithinNoise, 5)]);
        Assert.Contains(TweakLevels.Select(tweaks, TweakLevel.Safe, retested), tweak => tweak.Id == "game-mode");
        Assert.Equal(TweakEvidence.Documented, retested.Effective(tweaks.Single(tweak => tweak.Id == "game-mode")));
    }

    [Fact]
    public void LocalEvidenceRoundTripsAndIgnoresABrokenFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaisBoost.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var log = new ListLog();
            Assert.Empty(LocalEvidence.Load(directory, log).Entries);
            new LocalEvidence([Entry("hags-on", Verdict.RealGain, 4)]).Save(directory);
            Assert.Equal(Verdict.RealGain, LocalEvidence.Load(directory, log).Latest("hags-on")?.Verdict);

            File.WriteAllText(Path.Combine(directory, LocalEvidence.FileName), "{ broken");
            Assert.Empty(LocalEvidence.Load(directory, log).Entries);
            Assert.Single(log.Messages);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    // ------------------------------------------------------------------ DPC/ISR latency per driver (ETW replay)

    private static KernelLatencyReport Replay()
    {
        var accumulator = new KernelLatencyAccumulator();
        new ReplayKernelTrace(Path.Combine(AppContext.BaseDirectory, "Fixtures", "kernel-trace.json")).Record(TimeSpan.FromSeconds(20), accumulator.Add);
        return accumulator.Build(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void RecordedTraceRanksDriversByLongestAndTotalTime()
    {
        var report = Replay();
        Assert.Equal(884, report.Events);
        Assert.Equal(["nvlddmkm.sys", "ndis.sys", "storport.sys", "ntoskrnl.exe", KernelLatencyAnalysis.UnknownModule],
            report.ByMax.Select(driver => driver.Module));
        Assert.Equal(["nvlddmkm.sys", "storport.sys", "ndis.sys", "ntoskrnl.exe", KernelLatencyAnalysis.UnknownModule],
            report.ByTotal.Select(driver => driver.Module));

        var nvidia = report.ByMax[0];
        Assert.Equal(1250.5, nvidia.MaxDpcMicroseconds);
        Assert.Equal(30, nvidia.MaxIsrMicroseconds);
        Assert.Equal((201, 50), (nvidia.DpcCount, nvidia.IsrCount));
        Assert.Equal(8400 + 1250.5 + 1500, nvidia.TotalMicroseconds, 3);
        Assert.Contains("vídeo NVIDIA", nvidia.Hint);
        Assert.Equal(1250.5, report.MaxMicroseconds);
    }

    [Fact]
    public void DriversOver500MicrosecondsAreFlagged()
    {
        var findings = Replay().Findings;
        Assert.Collection(findings,
            finding =>
            {
                Assert.Equal(Severity.Critical, finding.Severity);
                Assert.StartsWith("nvlddmkm.sys", finding.Title);
                Assert.Contains("µs", finding.Title);
            },
            finding =>
            {
                Assert.Equal(Severity.Warning, finding.Severity);
                Assert.StartsWith("ndis.sys", finding.Title);
                Assert.Contains("rede", finding.Recommendation);
            });
    }

    [Theory]
    [InlineData(499.9, null)]
    [InlineData(500, Severity.Warning)]
    [InlineData(999, Severity.Warning)]
    [InlineData(1000, Severity.Critical)]
    public void LatencyThresholds(double microseconds, Severity? expected) => Assert.Equal(expected, KernelLatencyAnalysis.Classify(microseconds));

    [Fact]
    public void ModuleNamesAndHints()
    {
        Assert.Equal("rt640x64.sys", KernelLatencyAnalysis.ModuleName(@"\SystemRoot\System32\drivers\rt640x64.sys"));
        Assert.Contains("Realtek", KernelLatencyAnalysis.Hint("rt640x64.sys"));
        Assert.Contains("BIOS", KernelLatencyAnalysis.Hint("ACPI.sys"));
        Assert.Contains("disco", KernelLatencyAnalysis.Hint("storport.sys"));
        Assert.Null(KernelLatencyAnalysis.Hint("mydriver.sys"));
    }

    [Fact]
    public void EmptyTraceHasNoFindings()
    {
        var report = KernelLatencyAnalysis.Analyze([], TimeSpan.FromSeconds(1));
        Assert.Empty(report.ByMax);
        Assert.Empty(report.Findings);
        Assert.Equal(0, report.MaxMicroseconds);
    }

    // ------------------------------------------------------------------ HTML report

    private static BenchmarkReportInput Report(string game = "Counter-Strike 2") => new("1.1.0",
        new DateTimeOffset(2026, 11, 27, 15, 30, 0, TimeSpan.Zero), game, "CPU",
        ["CPU: AMD Ryzen 5 7600X", "GPU: NVIDIA GeForce RTX 4070"], ["Prioridade para o programa em primeiro plano"],
        BenchmarkSession.Evaluate([Round(200, 120), Round(202, 121), Round(199, 119)], [Round(220, 140), Round(222, 141), Round(219, 139)], Seed),
        [Round(200, 120) with { Bottleneck = Bottleneck.CpuOrSystem }], [Round(220, 140)], [5, 6, 5, 12, 5], [4.5, 4.6, 4.5, 5, 4.4]);

    [Fact]
    public void ReportIsSelfContained()
    {
        var html = BenchmarkReport.Html(Report());
        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Ganho real", html);
        Assert.Contains("<svg", html);
        Assert.Contains("polyline", html);
        Assert.Contains("RTX 4070", html);
        Assert.Contains("processador ou sistema", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<link", html);
    }

    [Fact]
    public void ReportEscapesUserText()
    {
        var html = BenchmarkReport.Html(Report("<script>alert(1)</script>"));
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void ReportChartUsesInvariantNumbers()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
        try
        {
            var chart = BenchmarkReport.Chart([5.5, 6.25, 5.75], null)!;
            var points = System.Text.RegularExpressions.Regex.Match(chart, "points=\"([^\"]+)\"").Groups[1].Value;
            Assert.Matches(@"^(\d+(\.\d)?,\d+(\.\d)? ?)+$", points);
            Assert.Null(BenchmarkReport.Chart(null, [1]));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
        Assert.Equal("benchmark-20261127-153000.html", BenchmarkReport.FileName(new DateTimeOffset(2026, 11, 27, 15, 30, 0, TimeSpan.Zero)));
    }

    // ------------------------------------------------------------------ Bundled PresentMon

    [Fact]
    public void BuildScriptDownloadsThePinnedPresentMon()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "NovaisBoost.slnx")))
            root = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("Raiz do repositório não encontrada.");
        var script = File.ReadAllText(Path.Combine(root, "scripts", "build.ps1"));
        Assert.Contains($"\"{PresentMonTool.Version}\"", script);
        Assert.Contains(PresentMonTool.Sha256, script, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("^[0-9a-f]{64}$", PresentMonTool.Sha256);
        Assert.Contains("PresentMon", File.ReadAllText(Path.Combine(root, "THIRD_PARTY_NOTICES.md")));
    }

    // ------------------------------------------------------------------ fakes

    /// <summary>Replays a kernel trace saved as JSON (same events the ETW session produces).</summary>
    private sealed class ReplayKernelTrace(string path) : IKernelTrace
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        public void Record(TimeSpan duration, Action<KernelEvent> sink, CancellationToken cancel = default)
        {
            foreach (var item in JsonSerializer.Deserialize<KernelEvent[]>(File.ReadAllText(path), Options)!)
            {
                cancel.ThrowIfCancellationRequested();
                sink(item);
            }
        }
    }

    private sealed class ListLog : IActionLog
    {
        public List<string> Messages { get; } = [];
        public void Info(string message) => Messages.Add(message);
        public void Warning(string message) => Messages.Add(message);
        public void Error(string message) => Messages.Add(message);
    }
}
