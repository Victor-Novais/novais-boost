<#
.SYNOPSIS
    SÓ PARA MÁQUINA VIRTUAL. Reproduz o estrago típico de "pacotes de FPS" para testar o Modo Reparar (F14).
.DESCRIPTION
    Deixa o Windows no estado que o Modo Reparar precisa detectar e restaurar:
      - BCD: useplatformclock, disabledynamictick e tscsyncpolicy forçados
      - arquivo de paginação desligado
      - serviços BITS e Windows Update desativados
      - FeatureSettingsOverride = 3 (mitigações Spectre/Meltdown desligadas)
      - políticas NoAutoUpdate e DisableAntiSpyware
      - Win32PrioritySeparation fora da faixa, LargeSystemCache e DisablePagingExecutive ligados
      - TCP: autotuning desligado e RSS desligado
    Recusa rodar fora de uma VM (Hyper-V, VMware, VirtualBox, QEMU/KVM, Parallels) e sem -IUnderstandThisIsAVm.
    Faça um snapshot da VM antes. Depois: NOVAIS boost → [10] Modo Reparar → restaurar → reiniciar → --reparar sem itens.
.EXAMPLE
    pwsh .\scripts\vm-damage.ps1 -IUnderstandThisIsAVm
#>
param(
    [switch] $IUnderstandThisIsAVm
)
$ErrorActionPreference = "Stop"

$computer = Get-CimInstance Win32_ComputerSystem
$bios = Get-CimInstance Win32_BIOS
$fingerprint = "$($computer.Manufacturer) $($computer.Model) $($bios.SerialNumber) $($bios.Manufacturer)"
$isVm = $fingerprint -match 'Virtual Machine|VMware|VirtualBox|innotek|QEMU|KVM|Parallels|Xen|Bochs'
if (-not $isVm) {
    throw "Este script só roda em máquina virtual. Detectado: $fingerprint"
}
if (-not $IUnderstandThisIsAVm) {
    throw "Confirme com -IUnderstandThisIsAVm (e faça um snapshot da VM antes)."
}
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Rode como administrador."
}

Write-Host "VM detectada ($fingerprint). Estragando de propósito..." -ForegroundColor Yellow

bcdedit /set "{current}" useplatformclock yes | Out-Null
bcdedit /set "{current}" disabledynamictick yes | Out-Null
bcdedit /set "{current}" tscsyncpolicy legacy | Out-Null

$cs = Get-CimInstance Win32_ComputerSystem
Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $false }
Get-CimInstance Win32_PageFileSetting | Remove-CimInstance

Set-Service -Name BITS -StartupType Disabled
Set-Service -Name wuauserv -StartupType Disabled

$mm = "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"
Set-ItemProperty $mm -Name FeatureSettingsOverride -Value 3 -Type DWord
Set-ItemProperty $mm -Name FeatureSettingsOverrideMask -Value 3 -Type DWord
Set-ItemProperty $mm -Name LargeSystemCache -Value 1 -Type DWord
Set-ItemProperty $mm -Name DisablePagingExecutive -Value 1 -Type DWord
Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" -Name Win32PrioritySeparation -Value 0xFF -Type DWord

New-Item "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU" -Force | Out-Null
Set-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU" -Name NoAutoUpdate -Value 1 -Type DWord
New-Item "HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender" -Force | Out-Null
# With Tamper Protection on, Windows ignores this policy; the repair still removes the value.
Set-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender" -Name DisableAntiSpyware -Value 1 -Type DWord

netsh int tcp set global autotuninglevel=disabled | Out-Null
netsh int tcp set global rss=disabled | Out-Null

Write-Host "Pronto. Reinicie a VM e rode: NOVAIS boost.exe --reparar" -ForegroundColor Green
