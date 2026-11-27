[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string] $ModsDirectory,
    [string] $ModuleFile = (Join-Path $PSScriptRoot 'OfContractsModule.dll')
)
$ErrorActionPreference = 'Stop'
$mods = (Resolve-Path -LiteralPath $ModsDirectory).ProviderPath
if (!(Test-Path -LiteralPath (Join-Path $mods 'mod.dll') -PathType Leaf)) {
    throw 'Select the Mods directory containing the installed overfiled-mod mod.dll.'
}
$source = (Resolve-Path -LiteralPath $ModuleFile).ProviderPath
$cache = Join-Path $mods 'cache'
[void][IO.Directory]::CreateDirectory($cache)
$manifest = Join-Path $cache 'manifest.json'
if ([IO.File]::Exists($manifest)) {
    $data = [IO.File]::ReadAllText($manifest) | ConvertFrom-Json
    if ($null -eq $data -or $data.modules -isnot [System.Array]) {throw 'Invalid cache manifest; left untouched.'}
} else {
    $data = [pscustomobject]@{ modules = @() }
}
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
$other = @($data.modules | Where-Object { $_.id -ne 'ofsf_catalog' })
$data.modules = @($other + [pscustomobject]@{ id='ofsf_catalog'; version='0.1.0'; hash=$hash; priority=120; enabled=$true })
$destination = Join-Path $cache 'ofsf_catalog.dll'
$key = [Guid]::NewGuid().ToString('N')
$tmpModule = Join-Path $cache ($key + '.dll.tmp')
$tmpManifest = Join-Path $cache ($key + '.json.tmp')
function Publish-File($temp, $target) {
    if ([IO.File]::Exists($target)) { [IO.File]::Replace($temp, $target, ($target + '.ofsf-backup')) }
    else { [IO.File]::Move($temp, $target) }
}
try {
    [IO.File]::Copy($source, $tmpModule)
    [IO.File]::WriteAllText($tmpManifest, ($data | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
    Publish-File $tmpModule $destination
    Publish-File $tmpManifest $manifest
} finally {
    foreach ($file in @($tmpModule, $tmpManifest)) { if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) } }
}
Write-Output "Installed ofsf_catalog in $cache ($hash)."
