namespace NovaisBoost.Core;

/// <summary>F19 diagnosis: disk reliability, network adapter tuning, interrupt affinity, USB layout, game storage.</summary>
public static class DeviceRules
{
    public static void Evaluate(HardwareSnapshot pc, List<Finding> findings)
    {
        if (pc.Deep is not { } deep)
            return;
        Disks(deep, findings);
        Nics(deep, findings);
        Affinity(deep, findings);
        Usb(deep, findings);
        Games(pc, deep, findings);
    }

    private const string ReliabilitySource = @"root\Microsoft\Windows\Storage: MSFT_StorageReliabilityCounter";

    private static void Disks(DeepInfo deep, List<Finding> findings)
    {
        foreach (var disk in deep.Reliability ?? [])
        {
            if (disk.Wear is >= 90)
                findings.Add(new("disk-wear", Severity.Critical, $"SSD perto do fim da vida útil: {disk.Name} ({disk.Wear}% usado)",
                    "O fabricante garante a gravação até 100%. Depois disso, o SSD pode virar somente leitura ou perder dados.",
                    "Faça backup agora e planeje a troca do SSD.", ReliabilitySource));
            else if (disk.Wear is >= 70)
                findings.Add(new("disk-wear", Severity.Warning, $"SSD com {disk.Wear}% da vida útil usada: {disk.Name}",
                    "Ainda funciona normalmente, mas já gastou boa parte da resistência a gravações.", "Mantenha backup em dia.", ReliabilitySource));
            if (disk.ReadErrorsUncorrected is > 0)
                findings.Add(new("disk-read-errors", Severity.Critical, $"Erros de leitura não corrigidos: {disk.Name} ({disk.ReadErrorsUncorrected})",
                    "O disco não conseguiu ler alguns dados. Costuma ser o primeiro sinal de falha.", "Faça backup agora e troque o disco.", ReliabilitySource));
            if (disk.TemperatureC is >= 70)
                findings.Add(new("disk-temperature", Severity.Warning, $"Disco quente: {disk.Name} a {disk.TemperatureC} °C",
                    "NVMe acima de ~70 °C reduz a velocidade para se proteger (thermal throttling), o que vira travada ao carregar mapas.",
                    "Use o dissipador do slot M.2 da placa-mãe e melhore o fluxo de ar do gabinete.", ReliabilitySource));
            if (disk.WriteCacheEnabled == false)
                findings.Add(new("disk-write-cache", Severity.Info, $"Cache de gravação desligado: {disk.Name}",
                    "Sem o cache, gravações pequenas ficam bem mais lentas.",
                    "Gerenciador de Dispositivos → Unidades de disco → Propriedades → Políticas → \"Habilitar cache de gravação\" (só com no-break ou notebook).",
                    "IOCTL_DISK_GET_CACHE_INFORMATION"));
        }
    }

    private static void Nics(DeepInfo deep, List<Finding> findings)
    {
        foreach (var nic in deep.Nics ?? [])
        {
            if (nic.RssEnabled == false)
                findings.Add(new("nic-rss-off", Severity.Warning, $"RSS desligado na placa de rede {nic.Name}",
                    "Sem Receive Side Scaling, todo o tráfego de rede é processado num único núcleo, que também atende o jogo.",
                    "Propriedades avançadas da placa de rede → \"Receive Side Scaling\" = Habilitado.", "Propriedades avançadas (*RSS)"));
            if (nic.ReceiveBuffers is { } buffers and < 256)
                findings.Add(new("nic-buffers", Severity.Info, $"Poucos buffers de recepção na placa {nic.Name} ({buffers})",
                    "Com poucos buffers, rajadas de pacotes podem ser descartadas.",
                    "Propriedades avançadas → \"Buffers de recepção\" no padrão do fabricante (512 ou mais).", "Propriedades avançadas (*ReceiveBuffers)"));
        }
    }

    private static void Affinity(DeepInfo deep, List<Finding> findings)
    {
        var forced = (deep.Affinity ?? []).Where(item => item.Policy != 0 || item.Mask is not null).ToArray();
        if (forced.Length == 0)
            return;
        var single = forced.Where(item => item.Mask is { } mask && ulong.PopCount(mask) == 1).GroupBy(item => item.Mask).Where(group => group.Count() > 1).ToArray();
        findings.Add(new("interrupt-affinity", single.Length > 0 ? Severity.Warning : Severity.Info,
            $"Afinidade de interrupções forçada em {forced.Length} dispositivo(s)",
            $"{string.Join(", ", forced.Select(item => item.Device))}. Normalmente é coisa de outro otimizador ou guia." +
            (single.Length > 0 ? " Vários dispositivos presos no mesmo núcleo disputam esse núcleo." : ""),
            "Sem medição (Provar ajustes) mostrando ganho, o padrão do Windows costuma ser melhor. Use o Microsoft Interrupt Affinity Tool para voltar.",
            @"HKLM\SYSTEM\CurrentControlSet\Enum\...\Interrupt Management\Affinity Policy"));
    }

    private static void Usb(DeepInfo deep, List<Finding> findings)
    {
        var devices = deep.Usb ?? [];
        foreach (var group in devices.GroupBy(device => device.Controller))
        {
            var mouse = group.FirstOrDefault(device => device.Kind == UsbDeviceKind.Mouse);
            var heavy = group.Where(device => device.Kind is UsbDeviceKind.Camera or UsbDeviceKind.Capture).ToArray();
            if (mouse is not null && heavy.Length > 0)
                findings.Add(new("usb-shared", Severity.Info, $"Mouse no mesmo controlador USB que {string.Join(", ", heavy.Select(device => device.Name))}",
                    $"Controlador: {group.Key}. Webcam e placa de captura usam muita banda e podem atrasar os relatórios do mouse.",
                    "Ligue a webcam/placa de captura numa porta de outro controlador (ex.: portas traseiras da placa-mãe × frontais do gabinete).",
                    "SetupAPI (CM_Get_Parent até o controlador PCI)"));
        }
    }

    private static void Games(HardwareSnapshot pc, DeepInfo deep, List<Finding> findings)
    {
        if (!pc.Volumes.Any(volume => volume.Nvme && volume.FreePercent > 15))
            return;
        var slower = (deep.Games ?? []).Where(game => game.Media == DiskMedia.Ssd && !game.Nvme).ToArray();
        if (slower.Length > 0)
            findings.Add(new("disk-game-sata", Severity.Info, $"Jogo em SSD SATA com um NVMe disponível: {string.Join(", ", slower.Select(game => game.Name))}",
                "O NVMe carrega mapas e texturas mais rápido que o SSD SATA (menos travada ao entrar na partida).",
                "Mova o jogo pelo próprio launcher (Steam: Armazenamento → Mover) para o NVMe.", "GameLibrary + MSFT_PhysicalDisk.BusType"));
    }
}

/// <summary>F19 mouse polling test: raw input timestamps while the mouse moves.</summary>
public sealed record PollingResult(double AverageHz, double StabilityPercent, int Samples);

public static class PollingRate
{
    /// <summary>
    /// Rate from the intervals between reports (only those shorter than 50 ms, i.e. while moving). Stability is the share of
    /// intervals within 25% of the median.
    /// </summary>
    public static PollingResult? Analyze(IReadOnlyList<double> timestampsMs)
    {
        var intervals = timestampsMs.Zip(timestampsMs.Skip(1), (a, b) => b - a).Where(gap => gap is > 0 and < 50).Order().ToArray();
        if (intervals.Length < 50)
            return null;
        var median = intervals[intervals.Length / 2];
        var stable = intervals.Count(gap => Math.Abs(gap - median) <= median * 0.25);
        return new PollingResult(1000 / intervals.Average(), 100d * stable / intervals.Length, intervals.Length);
    }

    /// <summary>The nominal setting closest to the measured rate (125, 250, 500, 1000, 2000, 4000, 8000 Hz).</summary>
    public static int Nominal(double hz) => new[] { 125, 250, 500, 1000, 2000, 4000, 8000 }.MinBy(rate => Math.Abs(Math.Log(rate / hz)));

    public static IReadOnlyList<Finding> Evaluate(PollingResult result, CpuInfo? cpu)
    {
        var findings = new List<Finding>();
        var nominal = Nominal(result.AverageHz);
        if (result.StabilityPercent < 80)
            findings.Add(new("mouse-polling-unstable", Severity.Warning, $"Polling do mouse instável ({result.StabilityPercent:0}% estável a ~{nominal} Hz)",
                "Intervalos irregulares entre os relatórios do mouse viram mira \"pulando\".",
                "Troque de porta USB (de preferência traseira, direto na placa-mãe), tire hubs e extensões e atualize o firmware do mouse."));
        if (nominal >= 4000 && cpu is { Cores: < 8 })
            findings.Add(new("mouse-polling-cpu", Severity.Info, $"Mouse a {nominal} Hz numa CPU de {cpu.Cores} núcleos",
                "Polling de 4.000/8.000 Hz gera milhares de interrupções por segundo e pode custar FPS em CPUs com poucos núcleos.",
                "Teste 1.000 ou 2.000 Hz com Provar ajustes: muitas vezes o FPS e o 1% low melhoram sem diferença perceptível na mira."));
        return findings;
    }
}

/// <summary>F19 streaming: bitrate from the measured upload, and the Guardian's OBS core split.</summary>
public static class StreamSettings
{
    /// <summary>Suggested video bitrate (kbps): 75% of the upload, capped by the platform.</summary>
    public static int SuggestBitrate(double uploadMbps, string platform)
    {
        var cap = platform.ToLowerInvariant() switch { "twitch" => 6000, "kick" => 8000, "youtube" => 20000, _ => 6000 };
        return (int)Math.Clamp(Math.Round(uploadMbps * 1000 * 0.75 / 500) * 500, 0, cap);
    }
}
