# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE in the repository root.

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CliPath,
    [Parameter(Mandatory)][string]$PlanTemplate,
    [Parameter(Mandatory)][string]$CredentialsPath,
    [Parameter(Mandatory)][string]$CheckoutRoot,
    [Parameter(Mandatory)][string]$PrivateRoot,
    [Parameter(Mandatory)][string]$ResultsRoot,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$SourceRevision,
    [switch]$ValidateOnly
)

$ErrorActionPreference = 'Stop'

function Test-CiContainedPath([string]$Path, [string]$Root) {
    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-CiPath([string]$Value, [string]$Prefix, [string]$Root) {
    if (-not $Value.StartsWith($Prefix, [StringComparison]::Ordinal)) { throw 'Required CI path prefix is missing.' }
    $relative = $Value.Substring($Prefix.Length).TrimStart('/')
    if ([IO.Path]::IsPathRooted($relative)) { throw 'Rooted suffix is not allowed.' }
    $resolved = [IO.Path]::GetFullPath((Join-Path $Root $relative))
    if (-not (Test-CiContainedPath $resolved $Root)) { throw 'Path escapes its configured root.' }
    return $resolved
}

try {
    $CheckoutRoot = [IO.Path]::GetFullPath($CheckoutRoot)
    $PrivateRoot = [IO.Path]::GetFullPath($PrivateRoot)
    $ResultsRoot = [IO.Path]::GetFullPath($ResultsRoot)
    foreach ($privatePath in @($PrivateRoot, $ResultsRoot, $PlanTemplate, $CredentialsPath)) {
        if (Test-CiContainedPath $privatePath $CheckoutRoot) { throw 'Private CI files must stay outside the checkout.' }
    }
    foreach ($required in @($CliPath, $PlanTemplate, $CredentialsPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw 'A required private configuration or tool file is missing.' }
    }
    $source = Join-Path $CheckoutRoot 'source'
    $actualRevision = & git -C $source rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $actualRevision.Trim() -ne $SourceRevision) { throw 'The checkout does not match the requested source revision.' }
    $plan = Get-Content -LiteralPath $PlanTemplate -Raw | ConvertFrom-Json
    $plan.SourceRoots = @($plan.SourceRoots | ForEach-Object { Resolve-CiPath $_ '${CHECKOUT}/' $CheckoutRoot })
    if ($plan.SourceRoots.Count -eq 0 -or -not ($plan.SourceRoots | Where-Object { Test-CiContainedPath $source $_ })) {
        throw 'The tested source checkout must be included in sourceRoots.'
    }
    foreach ($test in $plan.LocalTests) { $test.Project = Resolve-CiPath $test.Project '${CHECKOUT}/' $CheckoutRoot }
    foreach ($package in @($plan.TestPackage, $plan.ActualDriver)) {
        if ($null -eq $package) { continue }
        $package.Project = Resolve-CiPath $package.Project '${CHECKOUT}/' $CheckoutRoot
        $package.PackagePath = Resolve-CiPath $package.PackagePath '${CHECKOUT}/' $CheckoutRoot
    }
    foreach ($suite in @($plan.ProcessorSuites) + @($plan.LiveSuites)) {
        if ($null -ne $suite) { $suite.Inputs = @($suite.Inputs | ForEach-Object { Resolve-CiPath $_ '${PRIVATE}/' $PrivateRoot }) }
    }
    $runDirectory = Join-Path $ResultsRoot ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $resolvedPlan = Join-Path $runDirectory 'ResolvedPlan.json'
    $plan | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $resolvedPlan -Encoding utf8
    @{ SourceRevision = $SourceRevision; ValidationOnly = [bool]$ValidateOnly } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runDirectory 'SourceRevision.json') -Encoding utf8
    if ($ValidateOnly) {
        Write-Output 'CI path and revision validation passed; no processor command was sent.'
        exit 0
    }
    # Keep raw tests/logs and resolved plans private. Publish only a fixed status message.
    $arguments = @('workflow', '--plan', $resolvedPlan, '--settings', $CredentialsPath, '--results', (Join-Path $runDirectory 'workflow'))
    $log = Join-Path $runDirectory 'Cli.log'
    if ([IO.Path]::GetExtension($CliPath) -eq '.dll') { & dotnet $CliPath @arguments *> $log }
    else { & $CliPath @arguments *> $log }
    $result = $LASTEXITCODE
    if ($null -eq $result) { $result = 2 }
    $outcome = if ($result -eq 0) { 'passed' } else { 'failed or incomplete' }
    $message = "Processor hardware workflow $outcome. Raw evidence remains on the private runner."
    Write-Output $message
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $message }
    exit $result
}
catch {
    Write-Error 'Hardware CI could not complete. Check the private configuration, source revision, local evidence and processor lease before retrying.' -ErrorAction Continue
    exit 2
}
