namespace NovaisBoost.Core;

// ---------------------------------------------------------------- DPC/ISR latency per driver (F12)

public enum KernelEventKind { ImageLoad, Dpc, Isr }

/// <summary>
/// One event of a kernel trace. ImageLoad: a driver mapped at [Address, Address + Size). Dpc/Isr: a routine at
/// Address that ran for DurationMicroseconds.
/// </summary>
public sealed record KernelEvent(KernelEventKind Kind, ulong Address, double DurationMicroseconds = 0, ulong Size = 0, string? FileName = null);

/// <summary>Records DPC, ISR and image load events from the Windows kernel (ETW). The fake in the tests replays a recording.</summary>
public interface IKernelTrace
{
    /// <summary>Calls <paramref name="sink"/> for each event until <paramref name="duration"/> passes or the token is cancelled.</summary>
    void Record(TimeSpan duration, Action<KernelEvent> sink, CancellationToken cancel = default);
}

public sealed record DriverLatency(string Module, string? Hint, long DpcCount, long IsrCount,
    double MaxDpcMicroseconds, double MaxIsrMicroseconds, double TotalMicroseconds)
{
    public double MaxMicroseconds => Math.Max(MaxDpcMicroseconds, MaxIsrMicroseconds);
}

/// <param name="ByMax">Top drivers by their longest single DPC/ISR.</param>
/// <param name="ByTotal">Top drivers by total time spent in DPC/ISR.</param>
public sealed record KernelLatencyReport(TimeSpan Duration, long Events, IReadOnlyList<DriverLatency> ByMax,
    IReadOnlyList<DriverLatency> ByTotal, IReadOnlyList<Finding> Findings)
{
    public double MaxMicroseconds => ByMax.Count == 0 ? 0 : ByMax[0].MaxMicroseconds;
}

/// <summary>
/// Streams kernel events and keeps only per-routine totals (millions of DPCs per minute fit in a few KB).
/// Routines are mapped to drivers at the end, so the order of image and DPC events does not matter.
/// </summary>
public sealed class KernelLatencyAccumulator
{
    private readonly List<(ulong Base, ulong End, string Name)> _images = [];
    private readonly Dictionary<ulong, Routine> _routines = [];
    private long _events;

    public void Add(KernelEvent item)
    {
        switch (item.Kind)
        {
            case KernelEventKind.ImageLoad when item.Size > 0 && !string.IsNullOrWhiteSpace(item.FileName):
                _images.Add((item.Address, item.Address + item.Size, KernelLatencyAnalysis.ModuleName(item.FileName)));
                break;
            case KernelEventKind.Dpc or KernelEventKind.Isr when item.DurationMicroseconds >= 0 && double.IsFinite(item.DurationMicroseconds):
                _events++;
                if (!_routines.TryGetValue(item.Address, out var routine))
                    _routines[item.Address] = routine = new Routine();
                routine.Add(item.Kind, item.DurationMicroseconds);
                break;
        }
    }

    public KernelLatencyReport Build(TimeSpan duration)
    {
        var images = _images.OrderBy(image => image.Base).ToArray();
        string Resolve(ulong address)
        {
            // Last image that starts at or before the address, and contains it.
            int low = 0, high = images.Length - 1, found = -1;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (images[middle].Base <= address) { found = middle; low = middle + 1; }
                else high = middle - 1;
            }
            return found >= 0 && address < images[found].End ? images[found].Name : KernelLatencyAnalysis.UnknownModule;
        }

        var drivers = _routines.GroupBy(pair => Resolve(pair.Key), StringComparer.OrdinalIgnoreCase)
            .Select(group => new DriverLatency(group.Key, KernelLatencyAnalysis.Hint(group.Key),
                group.Sum(pair => pair.Value.DpcCount), group.Sum(pair => pair.Value.IsrCount),
                group.Max(pair => pair.Value.MaxDpc), group.Max(pair => pair.Value.MaxIsr), group.Sum(pair => pair.Value.Total)))
            .ToArray();
        var byMax = drivers.OrderByDescending(driver => driver.MaxMicroseconds).Take(KernelLatencyAnalysis.Top).ToArray();
        var byTotal = drivers.OrderByDescending(driver => driver.TotalMicroseconds).Take(KernelLatencyAnalysis.Top).ToArray();
        return new KernelLatencyReport(duration, _events, byMax, byTotal, KernelLatencyAnalysis.Evaluate(drivers));
    }

    private sealed class Routine
    {
        public long DpcCount, IsrCount;
        public double MaxDpc, MaxIsr, Total;

        public void Add(KernelEventKind kind, double microseconds)
        {
            Total += microseconds;
            if (kind == KernelEventKind.Dpc)
            {
                DpcCount++;
                MaxDpc = Math.Max(MaxDpc, microseconds);
            }
            else
            {
                IsrCount++;
                MaxIsr = Math.Max(MaxIsr, microseconds);
            }
        }
    }
}

public static class KernelLatencyAnalysis
{
    public const double WarningMicroseconds = 500;
    public const double CriticalMicroseconds = 1000;
    public const int Top = 10;
    public const string UnknownModule = "(desconhecido)";

    /// <summary>What a driver file usually is, to tell the user where to look.</summary>
    private static readonly (string Prefix, string Hint)[] Hints =
    [
        ("nvlddmkm", "driver de vídeo NVIDIA: atualize ou reinstale o driver da GPU"),
        ("amdkmdag", "driver de vídeo AMD: atualize ou reinstale o driver da GPU"),
        ("atikmdag", "driver de vídeo AMD: atualize ou reinstale o driver da GPU"),
        ("igdkmd", "driver de vídeo Intel: atualize o driver da GPU integrada"),
        ("dxgkrnl", "DirectX (vídeo): atualize o driver da GPU"),
        ("ndis", "rede: atualize o driver da placa de rede e desligue a economia de energia dela"),
        ("tcpip", "rede (pilha TCP/IP): verifique VPN, antivírus de rede e o driver da placa"),
        ("rt640x64", "placa de rede Realtek: atualize o driver pelo site da Realtek ou da placa-mãe"),
        ("rtwlan", "Wi-Fi Realtek: atualize o driver ou use cabo"),
        ("netwtw", "Wi-Fi Intel: atualize o driver ou use cabo"),
        ("netwlv", "Wi-Fi Intel: atualize o driver ou use cabo"),
        ("e1d", "placa de rede Intel: atualize o driver"),
        ("e2f", "placa de rede Intel: atualize o driver"),
        ("killer", "placa de rede Killer: atualize o driver e desligue o Killer Prioritization Engine"),
        ("storport", "disco: atualize o driver de armazenamento e o firmware do SSD"),
        ("stornvme", "SSD NVMe: atualize o firmware do SSD"),
        ("storahci", "disco SATA: atualize o driver do chipset"),
        ("iastor", "armazenamento Intel RST: atualize ou troque pelo driver padrão do Windows"),
        ("acpi", "BIOS/energia: atualize a BIOS e o driver do chipset; teste o plano de energia NOVAIS boost"),
        ("intelppm", "gerenciamento de energia da CPU: atualize a BIOS e o chipset"),
        ("amdppm", "gerenciamento de energia da CPU: atualize a BIOS e o chipset"),
        ("usbxhci", "controlador USB: troque o mouse/headset de porta e atualize o chipset"),
        ("usbport", "controlador USB: troque o dispositivo de porta e atualize o chipset"),
        ("wdf01000", "framework de drivers (vários dispositivos, muitas vezes USB): desconecte dispositivos um a um"),
        ("hdaudbus", "áudio HD: atualize o driver de áudio"),
        ("portcls", "áudio: atualize o driver de áudio e desligue realces"),
        ("ntoskrnl", "kernel do Windows (timers): normal em pequenas quantidades"),
    ];

    /// <summary>"\SystemRoot\System32\drivers\nvlddmkm.sys" → "nvlddmkm.sys".</summary>
    public static string ModuleName(string path)
    {
        var name = path.Replace('/', '\\');
        var slash = name.LastIndexOf('\\');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    public static string? Hint(string module) => Hints
        .FirstOrDefault(hint => module.StartsWith(hint.Prefix, StringComparison.OrdinalIgnoreCase)).Hint;

    public static KernelLatencyReport Analyze(IEnumerable<KernelEvent> events, TimeSpan duration)
    {
        var accumulator = new KernelLatencyAccumulator();
        foreach (var item in events)
            accumulator.Add(item);
        return accumulator.Build(duration);
    }

    public static Severity? Classify(double maxMicroseconds) => maxMicroseconds switch
    {
        >= CriticalMicroseconds => Severity.Critical,
        >= WarningMicroseconds => Severity.Warning,
        _ => null
    };

    internal static IReadOnlyList<Finding> Evaluate(IEnumerable<DriverLatency> drivers) => drivers
        .Where(driver => Classify(driver.MaxMicroseconds) is not null)
        .OrderByDescending(driver => driver.MaxMicroseconds)
        .Select(driver => new Finding($"dpc-driver-{driver.Module.ToLowerInvariant()}", Classify(driver.MaxMicroseconds)!.Value,
            $"{driver.Module}: DPC/ISR de até {driver.MaxMicroseconds:0} µs",
            $"Acima de {WarningMicroseconds:0} µs o driver pode causar travadas curtas, estalos no áudio e input lag " +
            $"({driver.DpcCount + driver.IsrCount} chamadas, {driver.TotalMicroseconds / 1000:0.0} ms no total).",
            driver.Hint is { } hint ? char.ToUpperInvariant(hint[0]) + hint[1..] + "."
                : "Atualize os drivers do chipset, da GPU, de rede e de áudio, e desconecte dispositivos USB um a um."))
        .ToArray();
}
