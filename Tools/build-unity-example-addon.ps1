param(
    [Parameter(Mandatory=$true)][string]$OutputPath,
    [string]$AddonId = 'example.mw4milsim',
    [string]$DisplayName = 'MW4 Milsim Example',
    [string]$MenuName = 'MW4 Example'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$unityRoot = Join-Path $projectRoot 'Templates\UnityModelAddon'
$exampleRoot = Join-Path $unityRoot 'Assets\Example'
$sourceManifest = Join-Path $projectRoot 'Addons\MW4Milsim\customsosig-model.json'
$sourceAnimations = Join-Path $projectRoot 'Templates\ModelAddon\animation-sources.json'
$target = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputPath))
if ($target -eq (Join-Path $projectRoot 'Addons\MW4Milsim') -or
    $target -eq $unityRoot -or $target -eq $exampleRoot) {
    throw 'OutputPath must be a new addon directory, not an existing source directory.'
}
if (Test-Path -LiteralPath $target) { throw "OutputPath already exists: $target" }
if ($AddonId -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]+$') { throw 'AddonId must be a unique, file-safe identifier.' }

$manifest = Get-Content -LiteralPath $sourceManifest -Raw | ConvertFrom-Json
$manifest.id = $AddonId
$manifest.displayName = $DisplayName
$manifest.menuName = $MenuName
$sources = Get-Content -LiteralPath $sourceAnimations -Raw | ConvertFrom-Json
foreach ($entry in $sources) {
    $folder = if ($entry.source -like 'animations/lpsp/*') { 'Weapon' } else { 'Mixamo' }
    $entry.source = Join-Path $exampleRoot "Animations\$folder\$([IO.Path]::GetFileName($entry.source))"
    if ($entry.bind) {
        $entry.bind = Join-Path $exampleRoot "Animations\Weapon\$([IO.Path]::GetFileName($entry.bind))"
    }
    if (-not (Test-Path -LiteralPath $entry.source)) { throw "Missing animation: $($entry.source)" }
    if ($entry.bind -and -not (Test-Path -LiteralPath $entry.bind)) { throw "Missing bind skeleton: $($entry.bind)" }
}

$model = Join-Path $exampleRoot 'Source\milsim.glb'
if (-not (Test-Path -LiteralPath $model)) { throw "Missing model: $model" }
foreach ($material in $manifest.materials) {
    foreach ($key in @('albedo', 'normal')) {
        if (-not $material.$key) { continue }
        $texture = Join-Path $exampleRoot ('Textures\' + [IO.Path]::GetFileName($material.$key))
        if (-not (Test-Path -LiteralPath $texture)) { throw "Missing texture: $texture" }
    }
}

New-Item -ItemType Directory -Force -Path (Join-Path $target 'assets') | Out-Null
$manifestPath = Join-Path $target 'customsosig-model.json'
$sourcesPath = Join-Path $target 'animation-sources.json'
$manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $manifestPath -Encoding utf8
$sources | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $sourcesPath -Encoding utf8
foreach ($material in $manifest.materials) {
    foreach ($key in @('albedo', 'normal')) {
        if (-not $material.$key) { continue }
        $name = [IO.Path]::GetFileName($material.$key)
        Copy-Item -LiteralPath (Join-Path $exampleRoot "Textures\$name") -Destination (Join-Path $target "assets\$name")
    }
}
foreach ($key in @('bloodMask', 'bloodNoise')) {
    if ($manifest.$key) {
        $name = [IO.Path]::GetFileName($manifest.$key)
        Copy-Item -LiteralPath (Join-Path $projectRoot "Addons\MW4Milsim\assets\$name") -Destination (Join-Path $target "assets\$name")
    }
}

Push-Location $projectRoot
$conversionLog = Join-Path $target 'conversion.log'
try {
    & (Join-Path $PSScriptRoot 'prepare-model-addon.ps1') -Manifest $manifestPath -ModelGlb $model -AnimationSources $sourcesPath *> $conversionLog
    if ($LASTEXITCODE -ne 0) { throw 'Addon conversion failed.' }
    Remove-Item -LiteralPath $conversionLog
} catch {
    if (Test-Path -LiteralPath $conversionLog) {
        Get-Content -LiteralPath $conversionLog -Tail 30 | Write-Host
    }
    throw
} finally {
    Pop-Location
}
Write-Host "Unity example addon prepared: $target"
