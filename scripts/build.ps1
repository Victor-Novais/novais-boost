<#
.SYNOPSIS
    Compila, testa e publica o NOVAIS boost como um único .exe.
.EXAMPLE
    pwsh .\scripts\build.ps1 -Version 1.0.1
    pwsh .\scripts\build.ps1 -Version 1.0.1 -CertificateThumbprint ABC123...   # assina o exe
#>
param(
    [string] $Version = "1.0.0",
    [string] $CertificateThumbprint,
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root "artifacts\publish"

Push-Location $root
try {
    dotnet build NovaisBoost.slnx -c Release -warnaserror -p:Version=$Version
    if ($LASTEXITCODE) { throw "Falha na compilação." }
    dotnet test NovaisBoost.slnx -c Release --no-build
    if ($LASTEXITCODE) { throw "Testes falharam." }

    Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish src/NovaisBoost.App -c Release -r win-x64 -o $output -p:Version=$Version -p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE) { throw "Falha na publicação." }

    $exe = Join-Path $output "NOVAIS boost.exe"
    Move-Item (Join-Path $output "NovaisBoost.App.exe") $exe -Force

    if ($CertificateThumbprint) {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $signtool) { throw "signtool.exe não encontrado (instale o Windows SDK)." }
        & $signtool.FullName sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $exe
        if ($LASTEXITCODE) { throw "Falha na assinatura." }
    }

    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash
    Set-Content (Join-Path $output "SHA256.txt") "$hash  NOVAIS boost.exe"
    Write-Host "Pronto: $exe ($([math]::Round((Get-Item $exe).Length / 1MB)) MB)  SHA256 $hash"
}
finally {
    Pop-Location
}
