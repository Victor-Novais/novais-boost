using System.Globalization;

namespace NovaisBoost.Core;

public enum Severity { Critical, Warning, Info }

public sealed record Finding(string Id, Severity Severity, string Title, string Detail, string Recommendation)
{
    public int Penalty => Severity switch { Severity.Critical => 15, Severity.Warning => 7, _ => 2 };
}

public sealed record DiagnosticReport(int Score, IReadOnlyList<Finding> Findings);

/// <summary>Pure rules over a hardware snapshot. No system access, fully testable.</summary>
public static class Diagnostics
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

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

        var ordered = findings.OrderBy(finding => finding.Severity).ThenBy(finding => finding.Id, StringComparer.Ordinal).ToArray();
        return new DiagnosticReport(Math.Clamp(100 - ordered.Sum(finding => finding.Penalty), 0, 100), ordered);
    }

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
    }

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

        if (discrete.Length > 0 && pc.Flags.HardwareGpuScheduling == false)
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

    private static string Kind(MemoryKind kind) => kind switch
    {
        MemoryKind.Ddr5 => "DDR5", MemoryKind.Ddr4 => "DDR4", MemoryKind.Ddr3 => "DDR3", _ => ""
    };

    public static string Gb(long bytes) => (bytes / 1_073_741_824d).ToString(bytes >= 10L * 1_073_741_824 ? "0" : "0.#", PtBr) + " GB";
}
