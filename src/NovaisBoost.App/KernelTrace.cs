using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>
/// Real-time kernel trace (ETW) of DPCs, ISRs and loaded drivers. Read-only: it observes the system and changes nothing.
/// Needs administrator. Uses its own session name (Windows 8+), so it does not stop LatencyMon or other kernel loggers.
/// </summary>
internal sealed class EtwKernelTrace(IActionLog log) : IKernelTrace
{
    private const string SessionName = "NOVAIS boost DPC";

    /// <summary>Kernel-mode addresses start here on x64; user-mode images are not drivers.</summary>
    private const ulong KernelSpace = 0xFFFF800000000000;

    public void Record(TimeSpan duration, Action<KernelEvent> sink, CancellationToken cancel = default)
    {
        if (TraceEventSession.IsElevated() != true)
            throw new UnauthorizedAccessException("A medição por driver precisa de administrador.");

        using var session = new TraceEventSession(SessionName) { StopOnDispose = true };
        session.EnableKernelProvider(KernelTraceEventParser.Keywords.DeferedProcedureCalls |
            KernelTraceEventParser.Keywords.Interrupt | KernelTraceEventParser.Keywords.ImageLoad);
        log.Info($"Gravação de DPC/ISR (ETW) iniciada por {duration.TotalSeconds:0} s.");

        var kernel = session.Source.Kernel;
        void Image(ImageLoadTraceData data)
        {
            if (data.ImageBase >= KernelSpace && data.ImageSize > 0)
                sink(new KernelEvent(KernelEventKind.ImageLoad, data.ImageBase, Size: (ulong)data.ImageSize, FileName: data.FileName));
        }
        kernel.ImageDCStart += Image;
        kernel.ImageLoad += Image;
        kernel.PerfInfoDPC += data => sink(new KernelEvent(KernelEventKind.Dpc, data.Routine, data.ElapsedTimeMSec * 1000));
        kernel.PerfInfoTimerDPC += data => sink(new KernelEvent(KernelEventKind.Dpc, data.Routine, data.ElapsedTimeMSec * 1000));
        kernel.PerfInfoISR += data => sink(new KernelEvent(KernelEventKind.Isr, data.Routine, data.ElapsedTimeMSec * 1000));

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        stop.CancelAfter(duration);
        using var registration = stop.Token.Register(() => session.Stop(noThrow: true));
        // Process() returns when the session stops.
        session.Source.Process();
        log.Info("Gravação de DPC/ISR (ETW) concluída.");
    }
}
