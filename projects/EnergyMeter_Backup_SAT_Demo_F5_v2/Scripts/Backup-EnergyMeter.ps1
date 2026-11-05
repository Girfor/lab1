[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDb,

    [Parameter(Mandatory = $true)]
    [string]$BackupDir
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SourceDb -PathType Leaf)) {
    throw "Исходная база данных не найдена: $SourceDb"
}

# Этот отдельный PowerShell-скрипт копирует остановленную SQLite-базу.
# Для копирования работающей БД используйте проект C# (SQLite BackupDatabase).
New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$target = Join-Path $BackupDir "energymeter-$stamp.sqlite"

Copy-Item -LiteralPath $SourceDb -Destination $target -ErrorAction Stop
$digest = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
$hashFile = "$target.sha256.txt"
"$digest  $(Split-Path -Path $target -Leaf)" |
    Set-Content -LiteralPath $hashFile -Encoding UTF8

Write-Host 'ENERGYMETER BACKUP'
Write-Host "Backup: $target"
Write-Host "SHA256: $digest"
Write-Host "Hash file: $hashFile"
Write-Host 'Backup created: PASS'
Write-Host 'Note: verify the restore before marking SAT-07 as passed.'
