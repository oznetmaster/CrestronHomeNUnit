# Copyright (c) 2026 Neil Colvin. Licensed under the MIT License; see LICENSE.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Target,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-f]{40}$')][string]$SourceRevision,
    [Parameter(Mandatory)][string]$CheckoutRoot,
    [ValidatePattern('^$|^[0-9a-f]{40}$')][string]$LibraryRevision = '',
    [switch]$PreflightOnly
)
$ErrorActionPreference = 'Stop'
$privateRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'CrestronHomeHardwareCI'
$runRoot = $null
$manifestPath = $null
$revisionState = $null

function Resolve-SourcePath([string]$Value) {
    if (!$Value.StartsWith('${CHECKOUT}/', [StringComparison]::Ordinal)) { throw 'Expected a source path placeholder.' }
    $relative = $Value.Substring('${CHECKOUT}/'.Length)
    if ([IO.Path]::IsPathRooted($relative)) { throw 'Unexpected rooted source path.' }
    $resolved = [IO.Path]::GetFullPath((Join-Path $CheckoutRoot $relative))
    $prefix = [IO.Path]::GetFullPath($CheckoutRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    if (!$resolved.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escapes checkout.' }
    return $resolved
}

try {
    $sourceOwner = (Get-Content "$PSScriptRoot/bridge-config.json" -Raw | ConvertFrom-Json).owner
    if ($sourceOwner -notmatch '^[A-Za-z0-9-]+$') { throw 'Invalid source repository owner.' }
    $targets = Get-Content "$PSScriptRoot/workflow-targets.json" -Raw | ConvertFrom-Json -AsHashtable
    if (!$targets.ContainsKey($Target)) { throw 'Unknown target.' }
    $selected = $targets[$Target]
    $source = Join-Path $CheckoutRoot 'source'
    $head = & git -C $source rev-parse HEAD
    if ($LASTEXITCODE -ne 0 -or $head -ne $SourceRevision) { throw 'Source revision mismatch.' }
    $origin = (& git -C $source remote get-url origin).Trim().TrimEnd('/') -replace '\.git$',''
    if ($LASTEXITCODE -ne 0 -or $origin -ne "https://github.com/$sourceOwner/$($selected.repository)") { throw 'Source repository mismatch.' }
    $credentials = Get-Content "$privateRoot/processor.settings.json" -Raw | ConvertFrom-Json
    $plan = Get-Content "$privateRoot/templates/$Target.json" -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($credentials.UserName) -or [string]::IsNullOrWhiteSpace($credentials.Password)) { throw 'Missing service credentials.' }
    if ($plan.ActualDriver -or @($plan.LiveSuites).Count -or @($plan.DeployedChecks).Count -or !$plan.RemoveTestInstanceAfterRun -or !$plan.RemoveTestPackageAfterSuccessfulRun -or $plan.AllowProcessorReboot) { throw 'Only automatically cleaned-up test-only plans are enabled.' }
    $runRoot = Join-Path $privateRoot ('results/' + $Target + '/' + [Guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($runRoot) | Out-Null
    @{SourceRepository=$selected.repository;SourceRevision=$SourceRevision;Target=$Target;PreflightOnly=[bool]$PreflightOnly} | ConvertTo-Json | Set-Content "$runRoot/SourceRevision.json" -Encoding utf8

    # Run source setup privately; it can include machine paths in diagnostics.
    if ($selected.kind -eq 'library') {
        if ($LibraryRevision) {
            # The bridge tests the candidate library, while retaining every other pinned dependency.
            $lockPath = Join-Path $source 'sources.lock.json'
            $sourceLock = Get-Content $lockPath -Raw | ConvertFrom-Json
            $entries = @($sourceLock.sources | Where-Object { $_.name -ceq $selected.sourceName })
            if ($entries.Count -ne 1 -or $entries[0].repository -cne "https://github.com/$sourceOwner/$($selected.libraryRepository).git") { throw 'Library source identity mismatch.' }
            $entries[0].revision = $LibraryRevision
            $sourceLock | ConvertTo-Json -Depth 20 | Set-Content $lockPath -Encoding utf8
            @{Repository=$selected.libraryRepository;Revision=$LibraryRevision;PackageRevision=$SourceRevision} | ConvertTo-Json | Set-Content "$runRoot/LibraryRevision.json" -Encoding utf8
        }
        & "$source/Initialize-Sources.ps1" *> "$runRoot/SourceSetup.log"
        if ($LASTEXITCODE -ne 0) { throw 'Pinned source setup failed.' }
        $sdk = Join-Path $source 'sources/CrestronHomeNUnit'
    } else {
        if ($LibraryRevision) { throw 'Library overrides are not allowed for driver workflows.' }
        $lock = Get-Content "$source/ProcessorTestSdk.lock.json" -Raw | ConvertFrom-Json
        if ($lock.repository -ne 'https://github.com/oznetmaster/CrestronHomeNUnit.git' -or $lock.revision -notmatch '^[0-9a-f]{40}$') { throw 'Invalid test SDK pin.' }
        $sdk = Join-Path $CheckoutRoot 'CrestronHomeNUnit'
        $createdSdk = $false
        if (!(Test-Path -LiteralPath $sdk)) {
            & git clone --no-checkout -- $lock.repository $sdk *> "$runRoot/SourceSetup.log"
            if ($LASTEXITCODE -ne 0) { throw 'SDK clone failed.' }
            $createdSdk = $true
        }
        if (!$createdSdk) {
            $dirty = & git -C $sdk status --porcelain
            if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Existing CI SDK checkout must be clean.' }
        }
        & git -C $sdk fetch origin $lock.revision *>> "$runRoot/SourceSetup.log"
        if ($LASTEXITCODE -ne 0) { throw 'SDK fetch failed.' }
        & git -C $sdk checkout --detach $lock.revision *>> "$runRoot/SourceSetup.log"
        if ($LASTEXITCODE -ne 0) { throw 'SDK checkout failed.' }
    }
    $env:ProcessorTestSdkRoot = $sdk
    $env:ManifestUtilExe = $env:CRESTRON_MANIFEST_UTIL
    if (!$env:ManifestUtilExe) { throw 'Set CRESTRON_MANIFEST_UTIL for the runner service.' }
    $env:IlRepackToolDirectory = Join-Path $privateRoot 'tools/ilrepack'
    $env:CompactJsonPath = Join-Path $privateRoot 'tools/Newtonsoft.Json.Compact.dll'
    foreach ($required in @($env:ManifestUtilExe, "$env:IlRepackToolDirectory/Mono.Cecil.dll", $env:CompactJsonPath)) {
        if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw 'A required CI packaging tool is unavailable.' }
    }
    if ($selected.repository -eq 'AppleTVCrestronDriver') {
        # These older driver merge scripts resolve the global tool and its Cecil library.
        $globalTools = Join-Path $env:USERPROFILE '.dotnet/tools'
        if (!(Test-Path (Join-Path $globalTools 'ilrepack.exe'))) {
            & dotnet tool install --global dotnet-ilrepack --version 2.0.45 *> "$runRoot/ToolSetup.log"
            if ($LASTEXITCODE -ne 0) { throw 'Service ILRepack installation failed.' }
        }
        if (!(Test-Path (Join-Path $globalTools '.store/dotnet-ilrepack/2.0.45'))) { throw 'Unexpected service ILRepack version.' }
        $env:PATH = $globalTools + [IO.Path]::PathSeparator + $env:PATH
        $env:MergeDependenciesContinueOnError = 'false'
        $helpers = Join-Path $CheckoutRoot 'AppleTVControlLibrary'
        $helperRevision = '5de886284cda4a6dd72ea6429189ce930470b41a'
        $createdHelpers = $false
        if (!(Test-Path -LiteralPath $helpers)) {
            & git clone --no-checkout -- https://github.com/oznetmaster/AppleTVControlLibrary.git $helpers *>> "$runRoot/SourceSetup.log"
            if ($LASTEXITCODE -ne 0) { throw 'Fake-device source clone failed.' }
            $createdHelpers = $true
        }
        $helperOrigin = (& git -C $helpers remote get-url origin).Trim()
        if ($LASTEXITCODE -ne 0 -or $helperOrigin -ne 'https://github.com/oznetmaster/AppleTVControlLibrary.git') { throw 'Unexpected fake-device source repository.' }
        if (!$createdHelpers) {
            $dirtyHelpers = & git -C $helpers status --porcelain
            if ($LASTEXITCODE -ne 0 -or $dirtyHelpers) { throw 'Existing fake-device source checkout must be clean.' }
        }
        & git -C $helpers fetch origin $helperRevision *>> "$runRoot/SourceSetup.log"
        if ($LASTEXITCODE -ne 0) { throw 'Fake-device source fetch failed.' }
        & git -C $helpers checkout --detach $helperRevision *>> "$runRoot/SourceSetup.log"
        if ($LASTEXITCODE -ne 0) { throw 'Fake-device source checkout failed.' }
        $plan.SourceRoots = @($plan.SourceRoots) + '${CHECKOUT}/AppleTVControlLibrary'
        Copy-Item -LiteralPath $env:CompactJsonPath -Destination "$source/Newtonsoft.Json.Compact.dll"
        Add-Content "$source/.git/info/exclude" '/Newtonsoft.Json.Compact.dll'
    }
    $plan.SourceRoots = @($plan.SourceRoots | ForEach-Object { Resolve-SourcePath $_ })
    foreach ($test in $plan.LocalTests) { $test.Project = Resolve-SourcePath $test.Project }
    $plan.TestPackage.Project = Resolve-SourcePath $plan.TestPackage.Project
    $plan.TestPackage.PackagePath = Resolve-SourcePath $plan.TestPackage.PackagePath
    $workflowProject = Resolve-SourcePath ('${CHECKOUT}/source/' + $selected.project)
    foreach ($required in @($plan.SourceRoots) + @($plan.LocalTests.Project) + @($plan.TestPackage.Project,$workflowProject)) {
        if (!(Test-Path -LiteralPath $required)) { throw 'A required source path was not resolved.' }
    }
    $settingsFile = Join-Path $runRoot 'Adapter.settings.json'
    $planFile = Join-Path $runRoot 'ResolvedPlan.json'
    $plan | ConvertTo-Json -Depth 30 | Set-Content $planFile -Encoding utf8
    @{PlanPath=$planFile;UserName=$credentials.UserName;Password=$credentials.Password} | ConvertTo-Json | Set-Content $settingsFile -Encoding utf8
    [Environment]::SetEnvironmentVariable($selected.settingsEnvironment,$settingsFile,'Process')
    if ($PreflightOnly) {
        & dotnet test $workflowProject --list-tests -c Release *> "$runRoot/Discovery.log"
        if ($LASTEXITCODE -ne 0) { throw 'Workflow discovery failed.' }
        $message='Service credentials, tools, pinned sources and workflow discovery validated. No processor command was sent.'
    } else {
        # Preserve this service's Debug counter across fresh CI checkouts.
        $manifestPath = [IO.Path]::ChangeExtension($plan.TestPackage.Project,'.json')
        $revisionState = Join-Path $privateRoot "state/$Target.version.txt"
        $manifest = [IO.File]::ReadAllText($manifestPath)
        $match = [regex]::Match($manifest,'"DriverVersion"\s*:\s*"(?<base>\d+\.\d+\.\d+)\.(?<revision>\d+)"')
        if (!$match.Success) { throw 'Missing processor package version.' }
        $revision = [int]$match.Groups['revision'].Value
        if (Test-Path $revisionState) {
            $previous=[version]([IO.File]::ReadAllText($revisionState).Trim())
            $current=[version]($match.Groups['base'].Value+'.0')
            if ($previous.Major -eq $current.Major -and $previous.Minor -eq $current.Minor -and $previous.Build -eq $current.Build) { $revision=[Math]::Max($revision,$previous.Revision) }
        }
        if ($revision -ge 65534) { throw 'Advance the package release version before continuing.' }
        $seed=$match.Groups['base'].Value+'.'+($revision+1).ToString('0000')
        [IO.File]::WriteAllText($manifestPath,[regex]::Replace($manifest,'(?<="DriverVersion"\s*:\s*")[^"]+', $seed))
        [IO.File]::WriteAllText($revisionState,$seed)
        & dotnet test $workflowProject -c Release --logger 'trx;LogFileName=adapter.trx' --results-directory $runRoot *> "$runRoot/Adapter.log"
        if ($LASTEXITCODE -ne 0) { throw 'Processor workflow failed or did not complete.' }
        [xml]$trx=Get-Content "$runRoot/adapter.trx" -Raw
        $counts=$trx.TestRun.ResultSummary.Counters
        if ([int]$counts.passed -lt 1 -or [int]$counts.passed -ne [int]$counts.total -or [int]$counts.failed -ne 0) { throw 'Incomplete or nonpassing adapter results.' }
        $leases=@(Get-ChildItem $runRoot -Recurse -Filter Lease.json)
        if ($leases.Count -ne 1 -or (Get-Content $leases[0].FullName -Raw | ConvertFrom-Json).State -ne 'Released') { throw 'The processor lease was not released.' }
        $workflows = @(Get-ChildItem $runRoot -Recurse -Filter Workflow.json)
        if ($workflows.Count -ne 1) { throw 'Expected one workflow completion receipt.' }
        $completed = Get-Content $workflows[0].FullName -Raw | ConvertFrom-Json
        if (!$completed.Passed -or !@($completed.Stages | Where-Object { $_.Stage -eq 'Remove temporary test package' -and $_.Outcome -eq 'Passed' }).Count) {
            throw 'Storage cleanup was not confirmed. Use adapter 1.3.0 or later and enable successful-run package cleanup.'
        }
        $message="Processor workflow passed: $($counts.passed) results; temporary instance and storage cleanup verified, and processor lease released. Catalogue cache may persist until the next planned reboot."
    }
    Write-Output $message
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content $env:GITHUB_STEP_SUMMARY $message }
    exit 0
} catch {
    if ($runRoot) { $_ | Out-String | Set-Content "$runRoot/Failure.log" -Encoding utf8 }
    Write-Error 'Hardware workflow failed or is incomplete. Inspect private evidence and the processor lease before retrying.' -ErrorAction Continue
    exit 2
} finally {
    if ($manifestPath -and $revisionState -and (Test-Path $manifestPath)) {
        $built=Get-Content $manifestPath -Raw | ConvertFrom-Json
        [IO.File]::WriteAllText($revisionState,$built.GeneralInformation.DriverVersion)
    }
}
