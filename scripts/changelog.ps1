<#
.SYNOPSIS
    Gera a seção do CHANGELOG.md de uma versão a partir dos commits desde a última tag (20.12).
.EXAMPLE
    pwsh .\scripts\changelog.ps1 -Version 1.3.0
#>
param([Parameter(Mandatory)] [string] $Version)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$last = git -C $root describe --tags --abbrev=0 2>$null
$range = if ($last) { "$last..HEAD" } else { "HEAD" }
$subjects = git -C $root log $range --pretty=format:%s --no-merges
$section = "## $Version ($(Get-Date -Format yyyy-MM-dd))`n`n" + (($subjects | Where-Object { $_.Trim() } | ForEach-Object { "- $_" }) -join "`n") + "`n"
$file = Join-Path $root "CHANGELOG.md"
$existing = if (Test-Path $file) { (Get-Content $file -Raw) -replace '^# Changelog\s*', '' } else { "" }
Set-Content $file "# Changelog`n`n$section`n$existing" -Encoding utf8
Write-Host "CHANGELOG.md atualizado para $Version. Depois: git tag v$Version"
