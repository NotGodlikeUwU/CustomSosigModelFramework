param([string]$ProfileRoot = 'E:\Games\r2mods\H3VR\profiles\Default')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$pluginsRoot = Join-Path $ProfileRoot 'BepInEx\plugins'
$distRoot = Join-Path $projectRoot 'dist\BepInEx\plugins'
$framework = Join-Path $distRoot 'NotGodlike-CustomSosigModelFramework'
if (-not (Test-Path -LiteralPath (Join-Path $framework 'CustomSosigModelFramework.dll'))) { throw 'Build the framework first.' }
if (Get-Process h3vr -ErrorAction SilentlyContinue) { throw 'Close H3VR before migrating/installing the framework.' }

$configRoot = Join-Path $ProfileRoot 'BepInEx\config'
$previousConfigs = @(
    (Join-Path $configRoot 'pochk.h3vr.customsosigmodelframework.cfg'),
    (Join-Path $configRoot 'pochk.h3vr.customsosigreplacer.cfg')
)
$newConfig = Join-Path $configRoot 'NotGodlike.h3vr.customsosigmodelframework.cfg'
if (-not (Test-Path -LiteralPath $newConfig)) {
    foreach ($previousConfig in $previousConfigs) {
        if (-not (Test-Path -LiteralPath $previousConfig)) { continue }
        Copy-Item -LiteralPath $previousConfig -Destination $newConfig
        break
    }
}
if (Test-Path -LiteralPath $newConfig) {
    # Preserve every setting, translating only the saved model identity.
    $contents = Get-Content -LiteralPath $newConfig -Raw
    $remapped = [Regex]::Replace($contents, '(?im)^(\s*ActiveModel\s*=\s*)pochk\.', '${1}NotGodlike.')
    if ($remapped -cne $contents) {
        [IO.File]::WriteAllText($newConfig, $remapped, (New-Object System.Text.UTF8Encoding($false)))
    }
}
New-Item -ItemType Directory -Force -Path $pluginsRoot | Out-Null
Copy-Item -LiteralPath $framework -Destination $pluginsRoot -Recurse -Force
foreach ($addon in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'Addons') -Directory) {
    if (-not (Test-Path -LiteralPath (Join-Path $addon.FullName 'customsosig-model.json'))) { continue }
    $source = Join-Path $distRoot ('NotGodlike-' + $addon.Name)
    Copy-Item -LiteralPath $source -Destination $pluginsRoot -Recurse -Force
    Write-Host "Installed addon: $($addon.Name)"
}

# Retire only the explicitly named earlier packages/configs. Keep a recoverable
# copy outside BepInEx so the old framework and renamed framework cannot load together.
$oldPackageNames = @('Pochk-CustomSosigModelFramework', 'Pochk-Metrocop', 'Pochk-MW4Milsim', 'Pochk-CustomSosigReplacer')
$oldPackages = @($oldPackageNames | ForEach-Object { Join-Path $pluginsRoot $_ })
$oldStaged = @($oldPackageNames | ForEach-Object { Join-Path $distRoot $_ })
$toArchive = @($oldPackages + $previousConfigs + $oldStaged | Where-Object { Test-Path -LiteralPath $_ })
if ($toArchive.Count -gt 0) {
    $backup = [IO.Path]::GetFullPath((Join-Path $projectRoot ('backups\creator-rename-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
    $backupRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'backups')).TrimEnd('\') + '\'
    if (-not $backup.StartsWith($backupRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected backup target.' }
    $allowedRoots = @($pluginsRoot, $configRoot, $distRoot) | ForEach-Object { [IO.Path]::GetFullPath($_).TrimEnd('\') + '\' }
    foreach ($item in $toArchive) {
        $resolved = [IO.Path]::GetFullPath($item)
        if (-not @($allowedRoots | Where-Object { $resolved.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) {
            throw "Unexpected archive source: $resolved"
        }
    }
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    foreach ($item in $toArchive) {
        $resolved = [IO.Path]::GetFullPath($item)
        $bucket = if ($resolved.StartsWith($allowedRoots[0], [StringComparison]::OrdinalIgnoreCase)) { 'installed' }
            elseif ($resolved.StartsWith($allowedRoots[1], [StringComparison]::OrdinalIgnoreCase)) { 'config' }
            else { 'staged' }
        $destinationRoot = Join-Path $backup $bucket
        New-Item -ItemType Directory -Force -Path $destinationRoot | Out-Null
        Move-Item -LiteralPath $item -Destination (Join-Path $destinationRoot (Split-Path -Leaf $item))
    }
    Write-Host "Previous creator-named packages and configs archived: $backup"
}
Write-Host 'Installed CustomSosigModelFramework. Choose Sosig, Random, or an installed addon in Wrist Menu > Custom Sosigs.'
