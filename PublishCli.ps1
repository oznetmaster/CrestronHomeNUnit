# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

param(
    [ValidateSet('win-x64','win-arm64')][string] $Runtime = 'win-x64',
    [string] $RuntimeVersion = '10.0.12',
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'artifacts\cli')
)
$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
# A new directory prevents stale settings or files from entering a later release.
$publishDirectory = Join-Path $outputRoot ('publish-' + $Runtime + '-' + [Guid]::NewGuid().ToString('N'))
& dotnet publish (Join-Path $PSScriptRoot 'CrestronHomeNUnit.Cli\CrestronHomeNUnit.Cli.csproj') `
    -c Release -r $Runtime --self-contained true -o $publishDirectory `
    -p:RuntimeFrameworkVersion=$RuntimeVersion -p:PublishTrimmed=false -p:PublishSingleFile=false `
    -p:DebugType=None -p:DebugSymbols=false -p:DeployAfterBuild=false
if ($LASTEXITCODE -ne 0) { throw 'CLI publish failed.' }
# Preserve the notices shipped with the exact runtime packs selected for this release.
$assets = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'CrestronHomeNUnit.Cli\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$runtimeNotices = Join-Path $publishDirectory 'licenses\bundled-runtime'
foreach ($pack in @('microsoft.netcore.app.runtime')) {
    $relativePack = "$pack.$Runtime/$RuntimeVersion"
    $packDirectory = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $relativePack } | Where-Object { Test-Path -LiteralPath $_ }) | Select-Object -First 1
    if (-not $packDirectory) { throw "Runtime pack not found: $relativePack" }
    $destination = Join-Path $runtimeNotices $pack
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $notices = @(Get-ChildItem -LiteralPath $packDirectory -File | Where-Object Name -Match '^(LICENSE(\.TXT)?|THIRD-PARTY-NOTICES\.TXT)$')
    if (-not @($notices | Where-Object Name -Match '^LICENSE').Count) { throw "Runtime license missing: $relativePack" }
    if ($pack -eq 'microsoft.netcore.app.runtime' -and -not @($notices | Where-Object Name -EQ 'THIRD-PARTY-NOTICES.TXT').Count) { throw 'Runtime third-party notices missing.' }
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $destination }
}
$privateFiles = @(Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Where-Object {
    $_.Name -match '(?i)(\.local\.json$|\.csproj\.user$|\.Local\.targets$|^LiveTestSettings\.json$|^ProcessorKeys\.dat$|\.pfx$|\.pdb$)'
})
if ($privateFiles.Count) { throw 'The publish directory contains private settings or development-only files.' }
foreach ($required in @('CrestronHomeNUnit.Cli.exe','LICENSE','THIRD-PARTY-NOTICES.md','licenses\NUnit-4.6.1-LICENSE.txt','licenses\DotNet-LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDirectory $required))) { throw "Missing release file: $required" }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $outputRoot ("CrestronHomeNUnit.Cli-$Runtime.zip")
$temporary = Join-Path $outputRoot ([Guid]::NewGuid().ToString('N') + '.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($publishDirectory, $temporary)
Move-Item -LiteralPath $temporary -Destination $archive -Force
$hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($archive + '.sha256'), $hash + '  ' + [IO.Path]::GetFileName($archive) + [Environment]::NewLine)
Write-Host "Self-contained CLI: $archive"
Write-Host "Published application directory: $publishDirectory"