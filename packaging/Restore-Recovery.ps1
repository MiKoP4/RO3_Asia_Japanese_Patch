param(
    [Parameter(Mandatory = $true)]
    [string]$GameClient
)

$ErrorActionPreference = 'Stop'
$GameClient = [System.IO.Path]::GetFullPath($GameClient)
$Recovery = Join-Path $GameClient 'ro3_Data\StreamingAssets\Recovery'
$Manifest = Join-Path $Recovery 'recovery-compatibility-manifest.json'
$State = Join-Path $GameClient 'BepInEx\config\RO3.RecoveryPatchState.txt'
$Targets = @(
    $Manifest,
    (Join-Path $Recovery 'LuaPayload\Localization_en.lua.bytes'),
    (Join-Path $Recovery 'LuaPayload\Localization_zh_CN.lua.bytes')
)

if (-not (Test-Path -LiteralPath $State -PathType Leaf)) {
    Write-Host '[Recovery] No Japanese-patch recovery state was found; nothing to restore.'
    exit 0
}

$StateValues = @{}
foreach ($line in Get-Content -LiteralPath $State) {
    if ($line -match '^([^=]+)=(.*)$') {
        $StateValues[$matches[1]] = $matches[2]
    }
}
$ExpectedManifestHash = $StateValues['PATCHED_MANIFEST_SHA256']
if ([string]::IsNullOrWhiteSpace($ExpectedManifestHash)) {
    throw 'Recovery patch state is missing PATCHED_MANIFEST_SHA256.'
}

$Backups = @($Targets | ForEach-Object { $_ + '.ro3-ja-original' })
$BackupCount = @($Backups | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count
if ($BackupCount -eq 0) {
    Remove-Item -LiteralPath $State -Force
    Write-Host '[Recovery] No original Recovery backups exist; state marker removed.'
    exit 0
}
if ($BackupCount -ne $Backups.Count) {
    throw "Recovery backup set is incomplete ($BackupCount/$($Backups.Count)); uninstall stopped."
}

if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) {
    throw 'Current Recovery manifest is missing; uninstall stopped.'
}
$CurrentManifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $Manifest).Hash
if (-not [string]::Equals(
        $CurrentManifestHash,
        $ExpectedManifestHash,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    # The game updater replaced Recovery after the last Japanese-patch run.
    # Restoring the older backups here would roll the game itself backwards.
    foreach ($backup in $Backups) {
        Remove-Item -LiteralPath $backup -Force
    }
    Remove-Item -LiteralPath $State -Force
    Write-Host '[Recovery] Current game Recovery changed after patching; stale backups discarded without restoring.'
    exit 0
}

for ($index = 0; $index -lt $Targets.Count; $index++) {
    Copy-Item -LiteralPath $Backups[$index] -Destination $Targets[$index] -Force
}
foreach ($backup in $Backups) {
    Remove-Item -LiteralPath $backup -Force
}
Remove-Item -LiteralPath $State -Force
Write-Host '[Recovery] Original localization payload and compatibility manifest restored.'
