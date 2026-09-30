param(
    [string]$GameRoot = 'H:\SteamLibrary\steamapps\common\H3VR',
    [string]$ProfileRoot = 'E:\Games\r2mods\H3VR\profiles\Default'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $projectRoot 'src\CustomSosigReplacer'
$outputRoot = Join-Path $projectRoot 'dist\BepInEx\plugins\NotGodlike-CustomSosigModelFramework'
$managedRoot = Join-Path $GameRoot 'h3vr_Data\Managed'
$bepinexCore = Join-Path $ProfileRoot 'BepInEx\core'
$csc = 'C:\Program Files\dotnet\sdk\5.0.214\Roslyn\bincore\csc.dll'

if (-not (Test-Path $csc)) { throw "C# compiler not found: $csc" }
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$references = @(
    (Join-Path $managedRoot 'mscorlib.dll'),
    (Join-Path $managedRoot 'System.dll'),
    (Join-Path $managedRoot 'System.Core.dll'),
    (Join-Path $managedRoot 'UnityEngine.dll'),
    (Join-Path $managedRoot 'UnityEngine.UI.dll'),
    (Join-Path $managedRoot 'Assembly-CSharp.dll'),
    (Join-Path $bepinexCore 'BepInEx.dll'),
    (Join-Path $bepinexCore '0Harmony.dll')
)

foreach ($reference in $references) {
    if (-not (Test-Path $reference)) { throw "Reference not found: $reference" }
}

$sources = Get-ChildItem -Path $sourceRoot -Filter '*.cs' -File | ForEach-Object FullName
$outputDll = Join-Path $outputRoot 'CustomSosigModelFramework.dll'
$arguments = @(
    $csc,
    '/nologo',
    '/target:library',
    '/optimize+',
    '/debug:portable',
    '/langversion:7.3',
    '/nostdlib+',
    "/out:$outputDll"
)
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += $sources

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed with exit code $LASTEXITCODE" }

# Framework is code-only. Addons are separate data packages.
$addonsRoot = Join-Path $projectRoot 'Addons'
foreach ($addon in Get-ChildItem -LiteralPath $addonsRoot -Directory) {
    $manifestPath = Join-Path $addon.FullName 'customsosig-model.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) { continue }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $destination = Join-Path $projectRoot ("dist\BepInEx\plugins\NotGodlike-" + $addon.Name)
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $files = @($manifest.mesh) + @($manifest.materials | ForEach-Object { $_.albedo; if ($_.normal) { $_.normal } }) + @($manifest.animations | ForEach-Object file)
    if ($manifest.bloodMask) { $files += $manifest.bloodMask }
    if ($manifest.bloodNoise) { $files += $manifest.bloodNoise }
    foreach ($relative in ($files | Select-Object -Unique)) {
        $resolved = [IO.Path]::GetFullPath((Join-Path $addon.FullName $relative))
        if (-not $resolved.StartsWith($addon.FullName + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Addon file escapes package: $relative" }
        $target = Join-Path $destination $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $resolved -Destination $target -Force
    }
    Copy-Item -LiteralPath $manifestPath -Destination $destination -Force
    foreach ($document in @('README.md', 'THIRD_PARTY_NOTICES.md')) {
        $path = Join-Path $addon.FullName $document
        if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination $destination -Force }
    }
    Write-Host "Addon: $destination"
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputRoot -Force
Write-Host "Framework: $outputDll"
