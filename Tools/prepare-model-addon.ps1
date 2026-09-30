param(
    [Parameter(Mandatory=$true)][string]$Manifest,
    [Parameter(Mandatory=$true)][string]$ModelGlb,
    [Parameter(Mandatory=$true)][string]$AnimationSources
)
$ErrorActionPreference = 'Stop'
$manifestPath = (Resolve-Path -LiteralPath $Manifest).Path
$modelPath = (Resolve-Path -LiteralPath $ModelGlb).Path
$addonRoot = Split-Path -Parent $manifestPath
$definition = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$sources = Get-Content -LiteralPath $AnimationSources -Raw | ConvertFrom-Json
function Get-AddonOutput([string]$relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $addonRoot $relative))
    if (-not $path.StartsWith($addonRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Asset path escapes addon.' }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    return $path
}
& node (Join-Path $PSScriptRoot 'export-skinned-glb.mjs') $modelPath (Get-AddonOutput $definition.mesh) $manifestPath
if ($LASTEXITCODE -ne 0) { throw 'Model conversion failed.' }
foreach ($animation in $definition.animations) {
    $matches = @($sources | Where-Object role -eq $animation.role)
    if ($matches.Count -ne 1) { throw "Expected one animation source for role $($animation.role)." }
    $source = (Resolve-Path -LiteralPath $matches[0].source).Path
    $bind = '-'
    if ($matches[0].bind) { $bind = (Resolve-Path -LiteralPath $matches[0].bind).Path }
    & node (Join-Path $PSScriptRoot 'export-retargeted-fbx-animation.mjs') $source $modelPath (Get-AddonOutput $animation.file) rig $bind $manifestPath
    if ($LASTEXITCODE -ne 0) { throw "Animation conversion failed: $($animation.role)." }
}
Write-Host 'Prepared mesh and animations. Place the declared PNG textures in the addon assets folder, then build/install.'
