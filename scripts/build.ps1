<#
.SYNOPSIS
    Compila, testa e publica o NOVAIS boost como um único .exe.
.EXAMPLE
    pwsh .\scripts\build.ps1 -Version 1.0.1
    pwsh .\scripts\build.ps1 -Version 1.0.1 -CertificateThumbprint ABC123...   # assina o exe (certificado OV/EV)
    pwsh .\scripts\build.ps1 -Version 1.0.1 -TrustedSigningMetadata .\signing.json -TrustedSigningDlib C:\tools\Azure.CodeSigning.Dlib.dll
#>
param(
    [string] $Version = "1.0.0",
    [string] $CertificateThumbprint,
    [string] $TimestampUrl = "http://timestamp.digicert.com",
    # Azure Trusted Signing (20.1): metadata.json { Endpoint, CodeSigningAccountName, CertificateProfileName } + Azure.CodeSigning.Dlib.dll
    [string] $TrustedSigningMetadata,
    [string] $TrustedSigningDlib,
    [string] $Repository = "Victor-Novais/novais-boost"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root "artifacts\publish"

# PresentMon (Intel, licença MIT) embutido em tools\. Versão e SHA256 fixos: iguais a PresentMonTool no Core
# (o teste BuildScriptDownloadsThePinnedPresentMon confere). Para atualizar, troque os dois lugares.
$presentMonVersion = "2.6.0"
$presentMonSha256 = "b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af"
$presentMonFile = "PresentMon-$presentMonVersion-x64.exe"
$presentMonUrl = "https://github.com/GameTechDev/PresentMon/releases/download/v$presentMonVersion/$presentMonFile"

function Get-PresentMon {
    $cache = Join-Path $root "artifacts\cache"
    New-Item -ItemType Directory -Force $cache | Out-Null
    $file = Join-Path $cache $presentMonFile
    if (-not (Test-Path $file) -or (Get-FileHash $file -Algorithm SHA256).Hash -ne $presentMonSha256) {
        Write-Host "Baixando $presentMonUrl"
        Invoke-WebRequest -Uri $presentMonUrl -OutFile "$file.download" -UseBasicParsing
        $hash = (Get-FileHash "$file.download" -Algorithm SHA256).Hash
        if ($hash -ne $presentMonSha256) {
            Remove-Item "$file.download" -Force
            throw "SHA256 do PresentMon não confere (esperado $presentMonSha256, recebido $hash)."
        }
        Move-Item "$file.download" $file -Force
    }
    return $file
}

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

    $tools = Join-Path $output "tools"
    New-Item -ItemType Directory -Force $tools | Out-Null
    Copy-Item (Get-PresentMon) (Join-Path $tools $presentMonFile) -Force
    Copy-Item (Join-Path $root "THIRD_PARTY_NOTICES.md") $output -Force

    if ($CertificateThumbprint -or $TrustedSigningMetadata) {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" | Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $signtool) { throw "signtool.exe não encontrado (instale o Windows SDK)." }
        if ($TrustedSigningMetadata) {
            if (-not $TrustedSigningDlib) { throw "Informe -TrustedSigningDlib (Azure.CodeSigning.Dlib.dll)." }
            & $signtool.FullName sign /v /fd SHA256 /tr "http://timestamp.acs.microsoft.com" /td SHA256 /dlib $TrustedSigningDlib /dmdf $TrustedSigningMetadata $exe
        }
        else {
            & $signtool.FullName sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $exe
        }
        if ($LASTEXITCODE) { throw "Falha na assinatura." }
    }
    else {
        Write-Warning "Exe NÃO assinado: o SmartScreen vai alertar e a atualização automática recusa versões sem assinatura."
    }

    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash
    Set-Content (Join-Path $output "SHA256.txt") "$hash  NOVAIS boost.exe`n$($presentMonSha256.ToUpperInvariant())  tools\$presentMonFile"

    # 20.3: manifest for the automatic update. Upload "NOVAIS boost.exe" (GitHub renames it NOVAIS.boost.exe) and latest.json
    # to the release v$Version; the app reads https://github.com/$Repository/releases/latest/download/latest.json.
    $manifest = [ordered]@{
        version = $Version
        url = "https://github.com/$Repository/releases/download/v$Version/NOVAIS.boost.exe"
        sha256 = $hash
        notes = "Veja o CHANGELOG.md da versão $Version."
    }
    $manifest | ConvertTo-Json | Set-Content (Join-Path $output "latest.json") -Encoding utf8
    Write-Host "Pronto: $exe ($([math]::Round((Get-Item $exe).Length / 1MB)) MB)  SHA256 $hash"
}
finally {
    Pop-Location
}
