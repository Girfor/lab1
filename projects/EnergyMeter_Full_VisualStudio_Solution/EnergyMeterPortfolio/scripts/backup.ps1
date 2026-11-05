param(
    [Parameter(Mandatory=$true)][string]$SourceDb,
    [Parameter(Mandatory=$true)][string]$BackupDir
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $SourceDb -PathType Leaf)) { throw 'Database not found' }
New-Item -Path $BackupDir -ItemType Directory -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $BackupDir "energymeter-$stamp.sqlite"
# For a live SQLite database, prefer sqlite3 .backup or VACUUM INTO under its
# own connection. Copy-Item below requires a stopped service / closed database.
Copy-Item -LiteralPath $SourceDb -Destination $target -ErrorAction Stop
$digest = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
"SHA256  $digest  $(Split-Path $target -Leaf)" |
    Set-Content -Path "$target.sha256.txt" -Encoding utf8
Write-Output "Backup: $target"
Write-Output "Verify restore on a separate host before marking this backup valid"
