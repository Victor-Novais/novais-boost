<#
.SYNOPSIS
    SÓ PARA MÁQUINA VIRTUAL. Teste automatizado de reversão (F20.6): aplica o nível 3 e "Reverter tudo", e confere que o
    registro voltou exatamente ao estado de antes.
.DESCRIPTION
    1. Exporta HKLM\SOFTWARE, HKLM\SYSTEM\CurrentControlSet e HKCU (reg export).
    2. "NOVAIS boost.exe" --aplicar 3 --confirmar  (cria ponto de restauração e backup como sempre)
    3. "NOVAIS boost.exe" --reverter-tudo --confirmar
    4. Exporta de novo e compara. Só as chaves que o Windows muda sozinho (lista $Ignore) podem diferir.
    Recusa rodar fora de uma VM e sem -IUnderstandThisIsAVm. Funciona no Windows Sandbox e no Hyper-V.
    Para testar ajustes que pedem reinício, rode com -Phase Apply, reinicie, e depois -Phase Revert.
.EXAMPLE
    pwsh .\scripts\vm-test.ps1 -IUnderstandThisIsAVm -Exe "C:\NOVAIS\NOVAIS boost.exe"
#>
param(
    [switch] $IUnderstandThisIsAVm,
    [string] $Exe = (Join-Path $PSScriptRoot "..\artifacts\publish\NOVAIS boost.exe"),
    [ValidateSet("All", "Apply", "Revert")] [string] $Phase = "All",
    [string] $WorkDir = "C:\NOVAIS-vm-test"
)
$ErrorActionPreference = "Stop"

$computer = Get-CimInstance Win32_ComputerSystem
$bios = Get-CimInstance Win32_BIOS
$fingerprint = "$($computer.Manufacturer) $($computer.Model) $($bios.SerialNumber) $($bios.Manufacturer)"
if ($fingerprint -notmatch 'Virtual Machine|VMware|VirtualBox|innotek|QEMU|KVM|Parallels|Xen|Bochs') {
    throw "Este script só roda em máquina virtual. Detectado: $fingerprint"
}
if (-not $IUnderstandThisIsAVm) { throw "Confirme com -IUnderstandThisIsAVm (e faça um snapshot da VM antes)." }
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Rode como administrador." }
if (-not (Test-Path $Exe)) { throw "Executável não encontrado: $Exe" }

# Values Windows changes by itself between two exports (timestamps, counters, caches).
$Ignore = 'LastUpdate|Timestamp|LastBoot|Bam\\|Prefetcher|SessionInfo|Diagnostics|RecentDocs|UserAssist|MuiCache|SRUM|WindowsUpdate\\Orchestrator|Explorer\\StartPage|CloudStore|TaskCache|RestorePoint|SystemRestore|ShellBags|BagMRU|WINEVT'
New-Item -ItemType Directory -Force $WorkDir | Out-Null

function Export-State([string] $Name) {
    $folder = Join-Path $WorkDir $Name
    New-Item -ItemType Directory -Force $folder | Out-Null
    foreach ($pair in @(@("HKLM\SOFTWARE", "software.reg"), @("HKLM\SYSTEM\CurrentControlSet", "system.reg"), @("HKCU", "user.reg"))) {
        reg export $pair[0] (Join-Path $folder $pair[1]) /y | Out-Null
    }
    Get-Service | Select-Object Name, StartType | Sort-Object Name | Export-Csv (Join-Path $folder "services.csv") -NoTypeInformation
    (powercfg /getactivescheme) | Set-Content (Join-Path $folder "power.txt")
}

function Compare-State {
    $differences = 0
    foreach ($file in "software.reg", "system.reg", "user.reg", "services.csv", "power.txt") {
        $before = Get-Content (Join-Path $WorkDir "before\$file") | Where-Object { $_ -notmatch $Ignore }
        $after = Get-Content (Join-Path $WorkDir "after\$file") | Where-Object { $_ -notmatch $Ignore }
        $diff = Compare-Object $before $after
        if ($diff) {
            $differences += $diff.Count
            $diff | Select-Object -First 40 | Format-Table -AutoSize | Out-String | Write-Host
            $diff | Out-File (Join-Path $WorkDir "diff-$file.txt")
        }
    }
    return $differences
}

if ($Phase -in "All", "Apply") {
    Export-State "before"
    & $Exe --aplicar 3 --confirmar
    if ($LASTEXITCODE -notin 0, 3) { throw "--aplicar falhou ($LASTEXITCODE)." }
    if ($Phase -eq "Apply") { Write-Host "Aplicado. Reinicie a VM e rode de novo com -Phase Revert."; return }
}
& $Exe --reverter-tudo --confirmar
if ($LASTEXITCODE) { throw "--reverter-tudo falhou ($LASTEXITCODE)." }
Export-State "after"
$count = Compare-State
if ($count -gt 0) {
    throw "$count linha(s) diferentes depois de Reverter tudo. Veja $WorkDir\diff-*.txt."
}
Write-Host "OK: o estado depois de Reverter tudo é igual ao de antes." -ForegroundColor Green
