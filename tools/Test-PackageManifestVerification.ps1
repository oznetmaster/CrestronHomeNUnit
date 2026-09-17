# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.
$ErrorActionPreference = 'Stop'
$verifier = Join-Path $PSScriptRoot '../CrestronHomeNUnit.Driver/VerifyPackageManifest.ps1'
$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$workspace = Join-Path $temporaryParent ('PackageMetadata-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($workspace) | Out-Null
$manifest = Join-Path $workspace 'Probe.json'
$metadata = @{ driverId='test-guid'; driverVersion='1.0.000.0002'; deviceType='Extension'; manufacturer='Example'; baseModel='Probe'; className='CrestronHomeNUnit.Driver.EntryPoint' }
$definition = @{ GeneralInformation=@{ Guid='test-guid'; DriverVersion='1.0.000.0002'; DeviceType='Extension'; Manufacturer='Example'; BaseModel='Probe' } }
function Write-Package([string]$name, [string]$version, [string[]]$entries) {
    $path = Join-Path $workspace ($name + '.pkg')
    $data = $metadata.Clone(); $data.driverVersion = $version
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries) {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($entry).Open())
            try { $writer.Write(($data | ConvertTo-Json -Compress)) } finally { $writer.Dispose() }
        }
    } finally { $zip.Dispose() }
    return $path
}
function Assert-Rejected([string]$path) {
    $rejected = $false
    try { & $verifier -ManifestPath $manifest -PkgPath $path -AssemblyName Probe | Out-Null }
    catch { $rejected = $true }
    if (!$rejected) { throw 'An invalid package passed metadata verification.' }
}
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $definition | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifest
    $stale = $metadata.Clone(); $stale.driverVersion = '1.0.000.0001'
    $stale | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'Probe.dat')
    $correct = Write-Package correct '1.0.000.0002' @('Probe.dat')
    & $verifier -ManifestPath $manifest -PkgPath $correct -AssemblyName Probe | Out-Null
    Assert-Rejected (Write-Package stale '1.0.000.0001' @('Probe.dat'))
    Assert-Rejected (Write-Package duplicate '1.0.000.0002' @('Probe.dat','Probe.dat'))
    Assert-Rejected (Write-Package caseDuplicate '1.0.000.0002' @('Probe.dat','probe.dat'))
    Assert-Rejected (Write-Package misplaced '1.0.000.0002' @('patched/Probe.dat'))
    Assert-Rejected (Write-Package missing '1.0.000.0002' @('Other.dat'))
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $workspace 'current.dat')
    & $verifier -ManifestPath $manifest -MetadataPath (Join-Path $workspace 'current.dat') | Out-Null
    Write-Output 'Package metadata regressions passed: authoritative package bytes, stale/duplicate/misplaced/missing entries, and legacy metadata input.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($workspace)
    if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\') -ne $temporaryParent.TrimEnd('\') -or [IO.Path]::GetFileName($resolved) -notlike 'PackageMetadata-*') {
        throw 'Refusing cleanup outside the generated temporary test directory.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
