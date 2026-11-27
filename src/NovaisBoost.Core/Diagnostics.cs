using System.Globalization;

namespace NovaisBoost.Core;

public enum Severity { Critical, Warning, Info }

public enum FindingCategory { Stability, Memory, Graphics, Storage, Display, Power, Windows, Network, Software }

/// <param name="Source">Where the data behind the finding comes from (WMI class, event log, registry...).</param>
public sealed record Finding(string Id, Severity Severity, string Title, string Detail, string Recommendation, string? Source = null)
{
    public int Penalty => Severity switch { Severity.Critical => 15, Severity.Warning => 7, _ => 2 };
    public FindingCategory Category => Diagnostics.CategoryOf(Id);
}

/// <param name="Categories">Points lost per category, already capped.</param>
public sealed record DiagnosticReport(int Score, IReadOnlyList<Finding> Findings)
{
    public IReadOnlyDictionary<FindingCategory, int> Categories { get; init; } = new Dictionary<FindingCategory, int>();
}

/// <summary>Pure rules over a hardware snapshot. No system access, fully testable.</summary>
public static class Diagnostics
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>
    /// Most points a category can take from the score (health score v2): ten disk warnings cannot zero the score,
    /// and stability problems weigh the most.
    /// </summary>
    public static readonly IReadOnlyDictionary<FindingCategory, int> CategoryCaps = new Dictionary<FindingCategory, int>
    {
        [FindingCategory.Stability] = 45, [FindingCategory.Memory] = 30, [FindingCategory.Graphics] = 30,
        [FindingCategory.Storage] = 25, [FindingCategory.Display] = 20, [FindingCategory.Power] = 20,
        [FindingCategory.Windows] = 15, [FindingCategory.Network] = 15, [FindingCategory.Software] = 10
    };

    public static DiagnosticReport Evaluate(HardwareSnapshot pc, DateOnly today)
    {
        var findings = new List<Finding>();
        Memory(pc, findings);
        Storage(pc, findings);
        Graphics(pc, today, findings);
        Displays(pc, findings);
        Power(pc, findings);
        Windows(pc, today, findings);
        Network(pc, findings);
        Stability(pc, findings);
        Software(pc, findings);

        var ordered = findings.OrderBy(finding => finding.Severity).ThenBy(finding => finding.Id, StringComparer.Ordinal).ToArray();
        var lost = ordered.GroupBy(finding => finding.Category)
            .ToDictionary(group => group.Key, group => Math.Min(CategoryCaps[group.Key], group.Sum(finding => finding.Penalty)));
        return new DiagnosticReport(Math.Clamp(100 - lost.Values.Sum(), 0, 100), ordered) { Categories = lost };
    }

    public static FindingCategory CategoryOf(string id) => id switch
    {
        "whea-fatal" or "whea-corrected" or "crashes" or "gpu-tdr" or "microcode-outdated" or "microcode-update" => FindingCategory.Stability,
        "memory-pressure" or "wsl-vm" or "nonpaged-pool" or "single-channel" or "ram-same-channel" or "xmp-expo" or "xmp-expo-likely" => FindingCategory.Memory,
        "hybrid-gpu" or "resizable-bar" or "hags" or "gpu-thermal" or "gpu-power-limit" => FindingCategory.Graphics,
        _ when id.StartsWith("gpu-", StringComparison.Ordinal) => FindingCategory.Graphics,
        "refresh-rate" or "display-hdmi" or "vrr-off" => FindingCategory.Display,
        "on-battery" or "power-plan" or "custom-power-plan" or "cpu-throttling" or "cpu-below-base" => FindingCategory.Power,
        "wifi" or "link-speed" => FindingCategory.Network,
        "overlays" or "other-optimizers" => FindingCategory.Software,
        _ when id.StartsWith("disk-", StringComparison.Ordinal) || id is "system-disk-space" or "system-hdd" or "game-on-hdd" or "trim-off"
            or "pagefile-off" or "pagefile-hdd" => FindingCategory.Storage,
        _ => FindingCategory.Windows
    };

    private static void Memory(HardwareSnapshot pc, List<Finding> findings)
    {
        if (pc.Memory is not { } memory)
            return;

        var used = memory.UsedPercent;
        if (used >= 80)
        {
            var top = string.Join(", ", pc.TopProcesses.Take(3).Select(process => $"{process.Name} ({Gb(process.WorkingSetBytes)})"));
            findings.Add(new Finding("memory-pressure", used >= 90 ? Severity.Critical : Severity.Warning,
                $"RAM quase cheia: {used:0}% em uso",
                top.Length > 0 ? $"Maiores consumidores: {top}." : "Pouca memória livre para jogos.",
                "Feche os programas listados ou use o Process Guardian para identificar consumo inútil."));
        }

        var vm = pc.TopProcesses.Where(process => process.Name.StartsWith("vmmem", StringComparison.OrdinalIgnoreCase))
            .MaxBy(process => process.WorkingSetBytes);
        if (vm is { WorkingSetBytes: >= 4 * 1_073_741_824L })
            findings.Add(new Finding("wsl-vm", Severity.Warning,
                $"Máquina virtual do WSL/Docker segurando {Gb(vm.WorkingSetBytes)} de RAM",
                "O Docker Desktop e o WSL reservam memória e não devolvem ao Windows enquanto estão abertos.",
                @"Feche o Docker Desktop antes de jogar (ou rode ""wsl --shutdown"") e limite o WSL em %UserProfile%\.wslconfig com [wsl2] memory=8GB."));

        if (memory.NonPagedPoolBytes > 2 * 1_073_741_824L)
            findings.Add(new Finding("nonpaged-pool", Severity.Info,
                $"Memória do kernel alta: {Gb(memory.NonPagedPoolBytes)} no pool não paginado",
                "É memória usada por drivers. Valores altos podem indicar vazamento de driver (rede, RGB, antivírus, captura).",
                "Atualize drivers de rede e chipset. Se esse valor crescer a cada dia, há um driver vazando memória."));

        if (memory.Modules.Count == 1)
            findings.Add(new Finding("single-channel", Severity.Warning, "RAM em canal único (1 pente)",
                "Com um único pente a memória trabalha em single channel, o que derruba o FPS em jogos competitivos.",
                "Use 2 pentes iguais nos slots indicados pelo manual da placa-mãe (geralmente A2 e B2)."));
        else if (memory.Modules.Count >= 2)
        {
            var patterns = DataCatalog.MemorySlots();
            var channels = memory.Modules.Select(module => KnownSoftware.ChannelOf(module, pc.Board?.Manufacturer, patterns)).ToArray();
            if (channels.All(channel => channel is not null) && channels.Distinct().Count() == 1)
                findings.Add(new Finding("ram-same-channel", Severity.Warning,
                    $"Pentes de RAM no mesmo canal ({channels[0]}): {string.Join(", ", memory.Modules.Select(module => module.Slot))}",
                    "Os pentes estão nos slots do mesmo canal, então a memória pode estar rodando em single channel mesmo com 2 pentes.",
                    "Pelo manual da placa-mãe, mova um pente para o outro canal (geralmente A2 e B2).",
                    "Win32_PhysicalMemory.DeviceLocator/BankLabel + data/memory-slots.json"));
        }

        var slow = memory.Modules.FirstOrDefault(module => module.RatedMts > 0 && module.ConfiguredMts > 0 && module.ConfiguredMts < module.RatedMts - 100);
        if (slow is not null)
        {
            findings.Add(new Finding("xmp-expo", Severity.Critical,
                $"RAM abaixo da velocidade nominal: {slow.ConfiguredMts} de {slow.RatedMts} MT/s",
                "O perfil XMP/EXPO está desligado ou não aplicado.",
                "Ative XMP (Intel) ou EXPO (AMD) na BIOS."));
            return;
        }

        var baseline = memory.Modules.FirstOrDefault(module =>
            module.Kind == MemoryKind.Ddr5 && module.ConfiguredMts is > 0 and <= 4800 ||
            module.Kind == MemoryKind.Ddr4 && module.ConfiguredMts is > 0 and <= 2666);
        if (baseline is not null)
            findings.Add(new Finding("xmp-expo-likely", Severity.Warning,
                $"RAM {Kind(baseline.Kind)} na velocidade padrão: {baseline.ConfiguredMts} MT/s",
                "Essa é a velocidade de fábrica (JEDEC). A maioria dos kits gamer suporta mais.",
                $"Confira a velocidade do seu kit{(baseline.PartNumber is { Length: > 0 } part ? $" ({part.Trim()})" : "")} e ative XMP/EXPO na BIOS."));
    }

    private static void Storage(HardwareSnapshot pc, List<Finding> findings)
    {
        foreach (var volume in pc.Volumes)
        {
            var free = volume.FreePercent;
            if (volume.IsSystem && free < 15)
                findings.Add(new Finding("system-disk-space", free < 10 ? Severity.Critical : Severity.Warning,
                    $"Disco do sistema ({volume.Letter}) com {free:0}% livre",
                    $"{Gb(volume.FreeBytes)} livres de {Gb(volume.TotalBytes)}. Pouco espaço deixa o SSD e o Windows mais lentos.",
                    "Libere espaço até ter pelo menos 15% livre (a limpeza agendada da F6 ajuda a manter)."));
            else if (!volume.IsSystem && free < 10)
                findings.Add(new Finding($"disk-space-{volume.Letter.TrimEnd(':').ToLowerInvariant()}", Severity.Info,
                    $"Disco {volume.Letter} com {free:0}% livre", $"{Gb(volume.FreeBytes)} livres de {Gb(volume.TotalBytes)}.",
                    "Libere espaço se houver jogos instalados nesse disco."));
        }

        foreach (var disk in pc.Disks.Where(disk => disk.Health is DiskHealth.Warning or DiskHealth.Unhealthy))
            findings.Add(new Finding("disk-health", Severity.Critical, $"Disco com problema de saúde: {disk.Name}",
                "O Windows reporta falhas neste disco.", "Faça backup dos seus arquivos agora e planeje a troca do disco."));

        if (pc.Disks.Any(disk => disk.IsSystem && disk.Media == DiskMedia.Hdd))
            findings.Add(new Finding("system-hdd", Severity.Critical, "Windows instalado em HD mecânico",
                "HD mecânico causa travadas e carregamentos lentos.", "Migre o Windows e os jogos para um SSD NVMe."));

        if (pc.Deep is not { } deep)
            return;
        var hasSsd = pc.Disks.Any(disk => disk.Media == DiskMedia.Ssd);
        var onHdd = (deep.Games ?? []).Where(game => game.Media == DiskMedia.Hdd).Select(game => game.Name).Distinct().ToArray();
        if (onHdd.Length > 0)
            findings.Add(new Finding("game-on-hdd", Severity.Warning, $"Jogo instalado em HD mecânico: {string.Join(", ", onHdd)}",
                "No HD os mapas e texturas carregam devagar e o jogo trava ao entrar em áreas novas (stutter de streaming).",
                hasSsd ? "Mova o jogo para o SSD pelo próprio launcher (Steam: Propriedades > Arquivos instalados > Mover)."
                    : "Instale um SSD NVMe e mova os jogos competitivos para ele.",
                "Pasta do jogo (GameLibrary) → MSFT_Partition → MSFT_PhysicalDisk.MediaType"));

        if (deep.TrimEnabled == false && hasSsd)
            findings.Add(new Finding("trim-off", Severity.Critical, "TRIM desligado com SSD instalado",
                "Sem TRIM o SSD perde velocidade de escrita com o tempo e se desgasta mais.",
                "Rode como administrador: fsutil behavior set DisableDeleteNotify 0",
                "fsutil behavior query DisableDeleteNotify"));

        if (deep.Pagefile is { } pagefile)
        {
            if (!pagefile.Automatic && pagefile.Files.Count == 0)
                findings.Add(new Finding("pagefile-off", Severity.Critical, "Arquivo de paginação desligado",
                    "Sem paginação, jogos e programas fecham sozinhos quando a RAM enche, mesmo com memória sobrando para o Windows.",
                    "Configurações > Sistema > Sobre > Configurações avançadas > Desempenho > Memória virtual: \"Gerenciar automaticamente\".",
                    "Win32_ComputerSystem.AutomaticManagedPagefile, Win32_PageFileUsage"));
            var hddFiles = pagefile.Files.Where(file => VolumeOf(pc, file.Path)?.Media == DiskMedia.Hdd).ToArray();
            if (hddFiles.Length > 0 && hasSsd)
                findings.Add(new Finding("pagefile-hdd", Severity.Warning, $"Arquivo de paginação em HD mecânico ({hddFiles[0].Path})",
                    "Quando a RAM enche, o Windows usa o disco da paginação. No HD isso trava o jogo.",
                    "Deixe a paginação gerenciada pelo sistema no SSD do Windows.",
                    "Win32_PageFileUsage + MSFT_PhysicalDisk.MediaType"));
        }
    }

    private static VolumeInfo? VolumeOf(HardwareSnapshot pc, string path) => pc.Volumes.FirstOrDefault(volume =>
        path.StartsWith(volume.Letter, StringComparison.OrdinalIgnoreCase));

    private static void Graphics(HardwareSnapshot pc, DateOnly today, List<Finding> findings)
    {
        var discrete = pc.Gpus.Where(gpu => !gpu.IsIntegrated).ToArray();
        if (discrete.Length > 0 && pc.Gpus.Any(gpu => gpu.IsIntegrated))
            findings.Add(new Finding("hybrid-gpu", Severity.Info,
                $"GPU integrada ativa junto com a {discrete[0].Name}",
                "Jogos, OBS e o navegador podem acabar rodando na GPU integrada.",
                "Ao otimizar, vamos fixar jogos e OBS na GPU dedicada. Se não usa a integrada, desative-a na BIOS."));

        foreach (var gpu in discrete)
        {
            if (gpu.DriverDate is { } date && today.DayNumber - date.DayNumber > 180)
                findings.Add(new Finding("gpu-driver-age", Severity.Warning,
                    $"Driver de vídeo antigo ({date.ToString("dd/MM/yyyy", PtBr)})",
                    $"{gpu.Name} com driver {gpu.DriverVersion ?? "?"}. Drivers novos trazem ganhos e correções para jogos recentes.",
                    "Atualize o driver da GPU (de preferência com instalação limpa)."));
            if (gpu.ResizableBar == false)
                findings.Add(new Finding("resizable-bar", Severity.Warning, "Resizable BAR desligado",
                    $"{gpu.Name} sem Resizable BAR/SAM ativo.", "Ative Above 4G Decoding e Resizable BAR na BIOS (CSM desligado)."));
        }

        foreach (var gpu in discrete)
        {
            if (gpu.Pcie is { } link && link.CurrentWidth > 0 && link.CurrentWidth < link.MaxWidth)
                findings.Add(new Finding("gpu-pcie-width", pc.Os.IsLaptop ? Severity.Info : Severity.Critical,
                    $"Placa de vídeo em PCIe x{link.CurrentWidth} (suporta x{link.MaxWidth})",
                    pc.Os.IsLaptop
                        ? $"{gpu.Name}: em notebook, menos linhas costuma ser o projeto do fabricante."
                        : $"{gpu.Name} está usando menos linhas PCIe do que suporta. Causas comuns: slot errado, riser, sujeira no contato ou M.2 dividindo as linhas.",
                    pc.Os.IsLaptop ? "Nada a fazer se o modelo do notebook é assim de fábrica."
                        : "Coloque a placa no primeiro slot x16 (o mais perto da CPU), sem riser, e confira no manual se algum M.2 divide as linhas com ele.",
                    "Win32_PnPEntity DEVPKEY_PciDevice_CurrentLinkWidth/MaxLinkWidth"));
            else if (gpu.Pcie is { } speed && speed.CurrentGeneration > 0 && speed.CurrentGeneration < speed.MaxGeneration)
                findings.Add(new Finding("gpu-pcie-gen", Severity.Info,
                    $"PCIe da placa de vídeo em Gen {speed.CurrentGeneration} agora (suporta Gen {speed.MaxGeneration})",
                    "Parada, a GPU reduz a geração do PCIe para economizar energia; isso é normal. Só importa se continuar baixa durante o jogo.",
                    "Se continuar baixa com o jogo aberto, confira na BIOS se o slot está em Auto ou na geração máxima.",
                    "Win32_PnPEntity DEVPKEY_PciDevice_CurrentLinkSpeed/MaxLinkSpeed"));

            if (gpu.Sensors is { } sensors)
            {
                if (sensors.ThermalLimited || sensors.TemperatureC >= 90)
                    findings.Add(new Finding("gpu-thermal", Severity.Warning, $"Placa de vídeo limitada por temperatura ({sensors.TemperatureC} °C)",
                        $"{gpu.Name} está reduzindo o clock para não esquentar mais.",
                        "Limpe a poeira, melhore a ventilação do gabinete e confira a curva das ventoinhas da GPU.",
                        "NVML: nvmlDeviceGetTemperature, nvmlDeviceGetCurrentClocksEventReasons"));
                else if (sensors.PowerLimited && sensors.UtilizationPercent >= 80)
                    findings.Add(new Finding("gpu-power-limit", Severity.Info, "Placa de vídeo no limite de energia sob carga",
                        $"{gpu.Name} a {sensors.UtilizationPercent}% de uso está no limite de energia. É comum em GPUs modernas, mas fontes fracas ou cabos adaptados pioram.",
                        "Confira se a fonte tem a potência recomendada e use cabos de energia separados (sem \"Y\").",
                        "NVML: nvmlDeviceGetUtilizationRates, nvmlDeviceGetCurrentClocksEventReasons"));
            }
        }

        if (discrete.Any(Compatibility.SupportsHags) && pc.Flags.HardwareGpuScheduling == false)
            findings.Add(new Finding("hags", Severity.Info, "Agendamento de GPU acelerado por hardware desligado",
                "O HAGS é necessário para Frame Generation (DLSS 3/4, FSR 3) e reduz a carga da CPU.",
                "Será ligado na otimização universal para GPUs compatíveis."));
    }

    private static void Displays(HardwareSnapshot pc, List<Finding> findings)
    {
        foreach (var display in pc.Displays.Where(display => display.CurrentHz > 1 && display.MaxHz >= display.CurrentHz + 10))
            findings.Add(new Finding("refresh-rate", Severity.Critical,
                $"Monitor a {display.CurrentHz} Hz, mas suporta {display.MaxHz} Hz",
                $"{display.Name} ({display.Width}x{display.Height}) está rodando abaixo da capacidade.",
                $"Configurações > Sistema > Tela > Avançado: escolha {display.MaxHz} Hz. Use cabo DisplayPort ou HDMI 2.1."));

        foreach (var display in pc.Displays.Where(display => display.Connection == DisplayConnection.Hdmi && display.MaxHz is > 1 and <= 120))
            findings.Add(new Finding("display-hdmi", Severity.Warning, $"Monitor por HDMI limitado a {display.MaxHz} Hz",
                $"{display.Name} ({display.Width}x{display.Height}) só oferece até {display.MaxHz} Hz por este cabo/porta. Muitos monitores gamer " +
                "só chegam a 144 Hz ou mais por DisplayPort ou HDMI 2.1.",
                "Se o monitor suporta mais Hz, use um cabo DisplayPort (ou HDMI 2.1 certificado) e escolha a taxa máxima nas Configurações de tela.",
                "QueryDisplayConfig (outputTechnology), EnumDisplaySettings"));

        if (pc.Deep?.WindowedVrr == false)
            findings.Add(new Finding("vrr-off", Severity.Info, "Taxa de atualização variável do Windows desligada",
                "A opção \"Taxa de atualização variável\" leva o G-Sync/FreeSync a jogos que não suportam VRR sozinhos.",
                "Configurações > Sistema > Tela > Gráficos > Alterar configurações padrão: ligue \"Taxa de atualização variável\".",
                @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences [DirectXUserGlobalSettings]"));
    }

    private static void Power(HardwareSnapshot pc, List<Finding> findings)
    {
        if (pc.Os.IsLaptop && pc.Os.OnBattery)
            findings.Add(new Finding("on-battery", Severity.Warning, "Notebook na bateria",
                "Na bateria o processador e a GPU são limitados.", "Jogue com o carregador conectado."));

        if (pc.PowerPlan is not { } plan)
            return;
        if (plan.Id == PowerPlanInfo.PowerSaver)
            findings.Add(new Finding("power-plan", Severity.Critical, "Plano de energia em Economia",
                "O processador fica limitado para economizar energia.", "Será trocado pelo plano NOVAIS boost na otimização."));
        else if (plan.Id == PowerPlanInfo.Balanced)
            findings.Add(new Finding("power-plan", Severity.Info, $"Plano de energia: {plan.Name}",
                "O plano equilibrado estaciona núcleos e reduz o clock em cargas leves.", "Será ajustado na otimização universal."));
        else if (!plan.IsWindowsDefault)
            findings.Add(new Finding("custom-power-plan", Severity.Info, $"Plano de energia personalizado: {plan.Name}",
                "Criado manualmente ou por outro otimizador. Ajustes de outros otimizadores podem conflitar entre si.",
                "Na otimização, o NOVAIS boost compara este plano com o dele e mostra as diferenças antes de trocar."));
    }

    private static void Windows(HardwareSnapshot pc, DateOnly today, List<Finding> findings)
    {
        if (pc.Flags.GameMode == false)
            findings.Add(new Finding("game-mode", Severity.Warning, "Modo de Jogo desligado",
                "O Modo de Jogo dá prioridade ao jogo e segura o Windows Update durante a partida.", "Será ligado na otimização universal."));
        if (pc.Flags.FastStartup == true)
            findings.Add(new Finding("fast-startup", Severity.Info, "Inicialização rápida ligada",
                "O Windows não reinicia de verdade ao desligar, o que acumula problemas de driver.", "Será desligada na otimização universal."));
        if (pc.Flags.MemoryIntegrity == true)
            findings.Add(new Finding("memory-integrity", Severity.Info, "Integridade de memória (VBS/HVCI) ligada",
                "Protege o sistema, mas custa alguns % de FPS.", "Só é desligada no nível Extremo, com aviso de segurança."));
        if (pc.Os.Uptime > TimeSpan.FromDays(7))
            findings.Add(new Finding("uptime", Severity.Info, $"PC ligado há {pc.Os.Uptime.Days} dias sem reiniciar",
                "Reiniciar limpa memória presa e aplica atualizações pendentes.", "Reinicie o PC pelo menos uma vez por semana."));
        if (pc.Board?.BiosDate is { } bios && today.DayNumber - bios.DayNumber > 730)
            findings.Add(new Finding("bios-age", Severity.Info, $"BIOS de {bios.ToString("MM/yyyy", PtBr)}",
                $"{pc.Board.Manufacturer} {pc.Board.Product}, versão {pc.Board.BiosVersion}. BIOS novas melhoram estabilidade de RAM e CPU.",
                "Verifique no site da fabricante se há uma versão mais nova."));
    }

    private static void Network(HardwareSnapshot pc, List<Finding> findings)
    {
        var connected = pc.Networks.Where(network => network.Connected).ToArray();
        if (connected.Length > 0 && connected.All(network => !network.Wired))
            findings.Add(new Finding("wifi", Severity.Warning, "Conectado só por Wi-Fi",
                "Wi-Fi tem mais ping, jitter e perda de pacote que cabo.", "Para jogar competitivo, use cabo de rede."));
        foreach (var wired in connected.Where(network => network.Wired && network.LinkBitsPerSecond is > 0 and < 1_000_000_000))
            findings.Add(new Finding("link-speed", Severity.Info, $"Rede cabeada a {wired.LinkBitsPerSecond / 1_000_000} Mbps",
                $"{wired.Name} não negociou 1 Gbps.", "Troque o cabo (Cat5e ou melhor) ou a porta do roteador."));
    }

    private static void Stability(HardwareSnapshot pc, List<Finding> findings)
    {
        if (pc.Deep is not { } deep)
            return;

        if (deep.Events is { } events)
        {
            const string source = "Log do Sistema: WHEA-Logger 17/18/19/47, WER-SystemErrorReporting 1001, Kernel-Power 41, Display 4101, nvlddmkm 13/14/153, %SystemRoot%\\Minidump";
            if (events.WheaFatal > 0)
                findings.Add(new Finding("whea-fatal", Severity.Critical, $"Erro fatal de hardware (WHEA) nos últimos {events.Days} dias: {events.WheaFatal}",
                    "O processador, a memória ou o PCIe reportaram um erro que travou o PC. Overclock, undervolt ou XMP/EXPO instáveis são as causas mais comuns.",
                    "Volte a BIOS para os padrões (ou desligue o overclock/undervolt), atualize a BIOS e teste a RAM (MemTest86) e a CPU.", source));
            else if (events.WheaCorrected > 0)
                findings.Add(new Finding("whea-corrected", Severity.Warning, $"Erros de hardware corrigidos (WHEA) nos últimos {events.Days} dias: {events.WheaCorrected}",
                    "O hardware se recuperou sozinho, mas erros corrigidos frequentes avisam instabilidade (RAM, CPU, PCIe ou riser).",
                    "Se aparecerem junto com travadas, teste sem overclock/XMP e com a BIOS atualizada.", source));

            var blueScreens = Math.Max(events.BugChecks, events.Minidumps ?? 0);
            var incidents = Math.Max(events.UnexpectedShutdowns, blueScreens);
            if (incidents > 0)
                findings.Add(new Finding("crashes", incidents > 2 ? Severity.Critical : Severity.Warning,
                    $"{incidents} desligamento(s) inesperado(s) ou tela(s) azul(is) nos últimos {events.Days} dias",
                    $"Telas azuis: {blueScreens}. Desligamentos sem aviso (Kernel-Power 41): {events.UnexpectedShutdowns}. " +
                    "Causas comuns: overclock instável, driver com defeito, temperatura ou fonte.",
                    "Desfaça overclocks, atualize drivers de vídeo e chipset e a BIOS. Desligamentos sem tela azul podem ser a fonte ou a temperatura.", source));

            var resets = events.DisplayResets + events.NvidiaDriverErrors;
            if (resets > 0)
                findings.Add(new Finding("gpu-tdr", Severity.Warning, $"O driver de vídeo parou de responder {resets} vez(es) nos últimos {events.Days} dias",
                    "O Windows reiniciou o driver da GPU (TDR). Isso aparece como tela preta ou travada de alguns segundos, ou o jogo fechando.",
                    "Faça uma instalação limpa do driver de vídeo, desfaça overclock da GPU e confira a temperatura e os cabos de energia da placa.", source));
        }

        if (deep.MicrocodeRevision is { } revision && pc.Cpu is { } cpu && cpu.Manufacturer.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var requirement in DataCatalog.Microcode().Where(item => item.Matches(cpu.Name)))
            {
                if (revision >= requirement.RecommendedRevision)
                    continue;
                var outdated = revision < requirement.MinimumRevision;
                findings.Add(new Finding(outdated ? "microcode-outdated" : "microcode-update", outdated ? Severity.Critical : Severity.Warning,
                    $"Microcódigo da CPU desatualizado: 0x{revision:X} ({(outdated ? "mínimo" : "recomendado")} 0x{(outdated ? requirement.MinimumRevision : requirement.RecommendedRevision):X})",
                    $"{requirement.Name}. {requirement.Detail}",
                    $"Atualize a BIOS da placa-mãe para uma versão com o microcódigo {requirement.Recommended} ou mais novo e use as \"Intel Default Settings\".",
                    @"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0 [Update Revision] + data/microcode.json"));
            }
        }
    }

    private static void Software(HardwareSnapshot pc, List<Finding> findings)
    {
        if (pc.Deep is not { } deep)
            return;
        if (deep.Overlays is { Count: >= 2 } overlays)
            findings.Add(new Finding("overlays", Severity.Info, $"{overlays.Count} overlays abertos ao mesmo tempo",
                $"{string.Join(", ", overlays)}. Cada overlay intercepta os quadros do jogo; vários juntos aumentam o tempo de quadro e causam conflitos.",
                "Deixe só o overlay que você usa (ex.: o do Discord ou o da NVIDIA) e desligue os outros nas configurações de cada programa.",
                "Processos em execução"));
        if (deep.Optimizers is { Count: > 0 } optimizers)
            findings.Add(new Finding("other-optimizers", Severity.Info, $"Outro otimizador instalado: {string.Join(", ", optimizers)}",
                "Vários otimizadores mexem nos mesmos ajustes e desfazem o trabalho um do outro. Alguns deixam alterações que pioram o PC.",
                "Use só um otimizador. Desinstale os outros e use o NOVAIS boost para revisar o que eles deixaram alterado.",
                "Programas instalados (Uninstall) e processos + data/optimizers.json"));
    }

    private static string Kind(MemoryKind kind) => kind switch
    {
        MemoryKind.Ddr5 => "DDR5", MemoryKind.Ddr4 => "DDR4", MemoryKind.Ddr3 => "DDR3", _ => ""
    };

    public static string Gb(long bytes) => (bytes / 1_073_741_824d).ToString(bytes >= 10L * 1_073_741_824 ? "0" : "0.#", PtBr) + " GB";
}
