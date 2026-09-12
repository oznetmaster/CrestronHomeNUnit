# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.

param(
    [Parameter(Mandatory)][string] $Version,
    [Parameter(Mandatory)][string] $ManifestUtilExe
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    # Compile all projects without producing another package or changing its version.
    dotnet build CrestronHomeNUnit.sln -c Release -p:CreateDriverPackage=false -p:DeployAfterBuild=false
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }
    dotnet build CrestronHomeNUnit.Driver/CrestronHomeNUnit.Driver.csproj -c Release "-p:ReleaseVersion=$Version" "-p:ManifestUtilExe=$ManifestUtilExe" "-p:LocalCrestronSdkLibDir=$(Split-Path $ManifestUtilExe -Parent)" -p:DeployAfterBuild=false
    if ($LASTEXITCODE -ne 0) { throw 'Processor package build failed.' }
    & ./CrestronHomeNUnit.TransportValidation/bin/Release/net10.0-windows/CrestronHomeNUnit.TransportValidation.exe
    if ($LASTEXITCODE -ne 0) { throw 'Runner regression validation failed.' }
    $package = Join-Path $root 'CrestronHomeNUnit.Driver/bin/Release/net472/CrestronHomeNUnit.Driver.pkg'
    $validation = Join-Path $root 'artifacts/release-validation'
    $extracted = Join-Path $validation ([Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory($package, $extracted)
    $assembly = Join-Path $extracted 'CrestronHomeNUnit.Driver.dll'
    foreach ($suite in @('self-tests','compatibility')) {
        $results = Join-Path $validation $suite
        & ./CrestronHomeNUnit.DesktopValidation/bin/Release/net472/CrestronHomeNUnit.DesktopValidation.exe $results $suite $assembly
        if ($LASTEXITCODE -ne 0) { throw "Packaged $suite failed." }
        [xml] $xml = Get-Content (Join-Path $results 'TestResult.xml') -Raw
        if ([int]$xml.'test-run'.passed -le 0 -or [int]$xml.'test-run'.failed -ne 0) { throw "Invalid $suite results." }
    }
    # Repeated self-test execution has a documented upstream NUnit 4.6.1 defect.
    # Release validation executes each included suite once; Verify.ps1 retains the repeated-run check.
    ./PublishRunner.ps1 -OutputDirectory (Join-Path $root 'artifacts/runner')
    if ($LASTEXITCODE -ne 0) { throw 'Runner publish failed.' }
    $release = Join-Path $root 'artifacts/release'
    if (Test-Path $release) { throw 'Release staging directory must be new.' }
    [IO.Directory]::CreateDirectory($release) | Out-Null
    Copy-Item -LiteralPath $package -Destination $release
    Copy-Item -LiteralPath (Join-Path $root 'artifacts/runner/CrestronHomeNUnit.Runner-win-x64.zip') -Destination $release
    $notices = Join-Path $root 'artifacts/release-notices'
    [IO.Directory]::CreateDirectory($notices) | Out-Null
    foreach ($file in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','RELEASE-NOTES.md','CHANGELOG.md','Validation.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $notices }
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $notices -Recurse
    Copy-Item -LiteralPath (Join-Path $root 'docs') -Destination $notices -Recurse
    # Preserve the source attributions and small reproductions linked from the guides.
    $linkedSources = @(git ls-files vendor/nunit/LICENSE.txt vendor/nunit/PROVENANCE.md tools/ILRepackIndexerRepro tools/NUnitRepeatRunReports)
    foreach ($file in $linkedSources) {
        $destination = Join-Path $notices $file
        [IO.Directory]::CreateDirectory((Split-Path $destination -Parent)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $destination
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($notices, (Join-Path $release 'CrestronHomeNUnit-Documentation.zip'))
    # Check release archives and the package before any upload.
    foreach ($archive in Get-ChildItem $release -File) {
        $zip = [IO.Compression.ZipFile]::OpenRead($archive.FullName)
        try {
            $private = @($zip.Entries | Where-Object FullName -Match '(?i)(\.local\.json$|\.csproj\.user$|\.Local\.targets$|(^|/)LiveTestSettings\.json$|ProcessorKeys\.dat$|\.pfx$)')
            if ($private.Count) { throw "Private file found in $($archive.Name)." }
        } finally { $zip.Dispose() }
    }
    $lines = @(Get-ChildItem $release -File | Sort-Object Name | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name })
    [IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'), $lines)
}
finally { Pop-Location }
