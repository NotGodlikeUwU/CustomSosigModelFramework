param([switch]$ExportBlend)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$addon = Join-Path $projectRoot 'Addons\MW4Milsim'
$manifestPath = Join-Path $addon 'customsosig-model.json'
$glbPath = Join-Path $addon 'milsim.glb'
$metrocopAssets = Join-Path $projectRoot 'Assets\metrocop'
$blender = Join-Path $PSScriptRoot '.blender-portable\blender-4.5.11-windows-x64\blender.exe'

if ($ExportBlend) {
    if (-not (Test-Path -LiteralPath $blender)) { throw "Portable Blender not found: $blender" }
    & $blender -b (Join-Path $projectRoot 'milsim.blend') --python (Join-Path $PSScriptRoot 'export-milsim-blend.py')
    if ($LASTEXITCODE -ne 0) { throw 'Blender model export failed.' }
}
if (-not (Test-Path -LiteralPath $manifestPath) -or -not (Test-Path -LiteralPath $glbPath)) {
    throw 'Export the supplied .blend before preparing animation assets.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$outputMesh = Join-Path $addon $manifest.mesh
& node (Join-Path $PSScriptRoot 'export-skinned-glb.mjs') $glbPath $outputMesh $manifestPath | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'MW4 Milsim mesh conversion failed.' }

foreach ($animation in $manifest.animations) {
    $metadataPath = Join-Path $metrocopAssets ($animation.file -replace '^assets/', '' -replace '\.gz$', '.json')
    if (-not (Test-Path -LiteralPath $metadataPath)) { throw "Missing source metadata for $($animation.role): $metadataPath" }
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    $source = (Resolve-Path -LiteralPath $metadata.source).Path
    $bind = '-'
    if ($metadata.sourceBind) { $bind = (Resolve-Path -LiteralPath $metadata.sourceBind).Path }
    $output = Join-Path $addon $animation.file
    Write-Host "Retargeting $($animation.role) from $source"
    & node (Join-Path $PSScriptRoot 'export-retargeted-fbx-animation.mjs') $source $glbPath $output rig $bind $manifestPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "MW4 Milsim animation failed: $($animation.role)" }
}
foreach ($name in @('blood-mask.png', 'blood-noise.png')) {
    Copy-Item -LiteralPath (Join-Path $projectRoot "Addons\Metrocop\assets\$name") -Destination (Join-Path $addon "assets\$name") -Force
}
Write-Host "MW4 Milsim addon assets prepared at $addon"
