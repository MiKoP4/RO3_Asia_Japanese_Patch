param([Parameter(Mandatory = $true)][string]$GameClient)
$ErrorActionPreference = 'Stop'
function Get-Sha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$GameClient = [IO.Path]::GetFullPath($GameClient)
if (-not (Test-Path -LiteralPath (Join-Path $GameClient 'ro3.exe') -PathType Leaf)) {
    throw 'Select the Client folder containing ro3.exe.'
}
$Launcher = Join-Path (Split-Path -Parent $GameClient) 'RO3AsiaLauncher.exe'
if (Get-Process ro3,RO3AsiaLauncher -ErrorAction SilentlyContinue | Where-Object {
    -not $_.Path -or $_.Path -eq (Join-Path $GameClient 'ro3.exe') -or $_.Path -eq $Launcher
}) {
    throw 'Close RO3 and RO3AsiaLauncher, then retry.'
}
# Disable before restoring: the old plugin rewrites these files during Awake.
$Plugin = Join-Path $GameClient 'BepInEx\plugins\RO3.LocalizationTablePatcher.dll'
if (Test-Path -LiteralPath $Plugin) {
    Move-Item -LiteralPath $Plugin -Destination ($Plugin + '.disabled-' + [Guid]::NewGuid().ToString('N'))
}
$Recovery = Join-Path $GameClient 'ro3_Data\StreamingAssets\Recovery'
$State = Join-Path $GameClient 'BepInEx\config\RO3.RecoveryPatchState.txt'
$Targets = @('recovery-compatibility-manifest.json', 'LuaPayload\Localization_en.lua.bytes',
    'LuaPayload\Localization_zh_CN.lua.bytes', 'LuaPayload\Localization_zh_TW.lua.bytes') |
    ForEach-Object { Join-Path $Recovery $_ }
$Backups = @($Targets | ForEach-Object { $_ + '.ro3-ja-original' })
$Count = @($Backups | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf }).Count
if ($Count -eq 0 -and -not (Test-Path -LiteralPath $State)) {
    Write-Host '[Recovery] No previous Recovery patch detected. Old patcher disabled if present.'
    exit 0
}
if ($Count -ne 4) {
    throw "Incomplete original backups ($Count/4). Old patcher disabled. Use official launcher repair; backups preserved."
}
$Expected = ''
if (Test-Path -LiteralPath $State) {
    foreach ($line in Get-Content -LiteralPath $State) {
        if ($line -match '^PATCHED_MANIFEST_SHA256=([0-9a-fA-F]{64})$') { $Expected = $matches[1] }
    }
}
$Current = Get-Sha256 $Targets[0]
$Original = Get-Sha256 $Backups[0]
if ($Current -ne $Original -and $Current -ne $Expected) {
    throw 'Unknown manifest version. Old patcher disabled. Use official launcher repair; no backups deleted.'
}
for ($i = 0; $i -lt $Targets.Count; $i++) {
    Copy-Item -LiteralPath $Backups[$i] -Destination $Targets[$i] -Force
    if ((Get-Sha256 $Targets[$i]) -ne (Get-Sha256 $Backups[$i])) {
        throw "Restore verification failed: $($Targets[$i])"
    }
}
Write-Host '[Recovery] Four files restored and SHA-256 verified. Backups and state preserved.'
Write-Host '[Recovery] Start through RO3AsiaLauncher, not ro3.exe directly.'
