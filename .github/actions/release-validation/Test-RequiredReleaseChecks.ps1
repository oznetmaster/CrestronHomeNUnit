# Copyright (c) 2026 Neil Colvin.
# Licensed under the terms in LICENSE in the repository root.
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" -DefinitionsOnly
$revision = 'a' * 40
$required = @([pscustomobject]@{context='External tests';appId=17})
$passing = [pscustomobject]@{id=1;name='External tests';app=@{id=17};head_sha=$revision;status='completed';conclusion='success'}
if ((Get-RequiredReleaseCheckState @($passing) $required $revision) -ne 'Passed') { throw 'Expected a passing exact-source check.' }
if ((Get-RequiredReleaseCheckState @() $required $revision) -ne 'Pending') { throw 'Missing checks must block release.' }
if ((Get-RequiredReleaseCheckState @($passing) $required ('b'*40)) -ne 'Pending') { throw 'Another source revision cannot pass.' }
if ((Get-RequiredReleaseCheckState @($passing) @([pscustomobject]@{context='External tests';appId=99}) $revision) -ne 'Pending') { throw 'Another App cannot pass.' }
foreach ($conclusion in @('failure','cancelled','skipped','neutral','timed_out','action_required')) {
    $failed = [pscustomobject]@{id=2;name='External tests';app=@{id=17};head_sha=$revision;status='completed';conclusion=$conclusion}
    if ((Get-RequiredReleaseCheckState @($passing,$failed) $required $revision) -ne 'Failed') { throw 'A newer failed or incomplete check must block release.' }
}
$pending = [pscustomobject]@{id=2;name='External tests';app=@{id=17};head_sha=$revision;status='in_progress';conclusion=$null}
if ((Get-RequiredReleaseCheckState @($passing,$pending) $required $revision) -ne 'Pending') { throw 'A newer pending check must block release.' }
$rejected = $false
try { Get-RequiredReleaseCheckState @($passing) @([pscustomobject]@{context='External tests';appId='17'}) $revision | Out-Null } catch { $rejected = $true }
if (!$rejected) { throw 'Invalid check configuration must be rejected.' }
Write-Output 'Release check policy: 12 scenarios passed.'

$checks = 0
function Verify([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
    $script:checks++
}
function Reject([scriptblock]$Action) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Verify $rejected 'An invalid release override was accepted.'
}
$repository = 'example/library'
$hostedPass = [pscustomobject]@{ id=1; head_sha=$revision; head_repository=@{full_name=$repository}; event='push'; status='completed'; conclusion='success' }
Verify ((Get-HostedReleaseCheckState @($hostedPass) $revision $repository) -eq 'Passed') 'Hosted checks should pass on the exact source.'
Verify ((Get-HostedReleaseCheckState @() $revision $repository) -eq 'Pending') 'Missing hosted tests must not pass.'
Verify ((Get-HostedReleaseCheckState @($hostedPass) ('b'*40) $repository) -eq 'Pending') 'Another revision cannot supply hosted validation.'
Verify ((Get-HostedReleaseCheckState @($hostedPass) $revision 'other/library') -eq 'Pending') 'Another repository cannot supply hosted validation.'
foreach ($conclusion in @('failure','cancelled','skipped','neutral','timed_out','action_required')) {
    $newer = [pscustomobject]@{ id=2; head_sha=$revision; head_repository=@{full_name=$repository}; event='workflow_dispatch'; status='completed'; conclusion=$conclusion }
    Verify ((Get-HostedReleaseCheckState @($hostedPass,$newer) $revision $repository) -eq 'Failed') 'Newer nonpassing hosted results must block.'
}
$newer = [pscustomobject]@{ id=2; head_sha=$revision; head_repository=@{full_name=$repository}; event='push'; status='in_progress'; conclusion=$null }
Verify ((Get-HostedReleaseCheckState @($hostedPass,$newer) $revision $repository) -eq 'Pending') 'An older hosted pass cannot hide a current run.'
$unsupported = [pscustomobject]@{ id=1; head_sha=$revision; head_repository=@{full_name=$repository}; event='pull_request_target'; status='completed'; conclusion='success' }
Verify ((Get-HostedReleaseCheckState @($unsupported) $revision $repository) -eq 'Pending') 'Unsupported event cannot supply hosted validation.'
Verify ($null -eq (Get-HardwareReleaseOverride '' '' 'push')) 'No override must remain the default.'
Verify ($null -eq (Get-HardwareReleaseOverride 'false' '' 'workflow_dispatch')) 'An unchecked input must retain hardware checks.'
Verify ((Get-HardwareReleaseOverride 'true' 'Local runner offline' 'workflow_dispatch') -eq 'Local runner offline') 'An explicit offline-runner override must be supported.'
Reject { Get-HardwareReleaseOverride 'yes' 'Reason' 'workflow_dispatch' }
Reject { Get-HardwareReleaseOverride 'true' '' 'workflow_dispatch' }
Reject { Get-HardwareReleaseOverride 'true' 'Reason' 'push' }
Reject { Get-HardwareReleaseOverride 'true' 'Reason' 'release' }
Reject { Get-HardwareReleaseOverride 'true' "Reason`n::error::injected" 'workflow_dispatch' }
Reject { Get-HardwareReleaseOverride 'true' ('a'*501) 'workflow_dispatch' }
$hardware = [pscustomobject]@{context='Processor tests / library';appId=17}
$other = [pscustomobject]@{context='Hosted approval';appId=19}
Verify (@(Select-ReleaseChecks @($hardware,$other) $false).Count -eq 2) 'Default policy must retain every check.'
$remaining = @(Select-ReleaseChecks @($hardware,$other) $true)
Verify ($remaining.Count -eq 1 -and $remaining[0].context -eq 'Hosted approval') 'Hardware override must retain other required checks.'
Verify ($null -eq (Get-DesktopReleaseOverride '' '' 'push')) 'Desktop reuse must be explicit.'
Verify ($null -eq (Get-DesktopReleaseOverride 'false' '' 'workflow_dispatch')) 'Unchecked desktop override must retain checks.'
Verify ((Get-DesktopReleaseOverride 'true' 'Manual Release tests passed; exact source and report recorded' 'workflow_dispatch') -like 'Manual Release*') 'Recorded desktop results must support a manual override.'
Reject { Get-DesktopReleaseOverride 'yes' 'Reason' 'workflow_dispatch' }
Reject { Get-DesktopReleaseOverride 'true' '' 'workflow_dispatch' }
Reject { Get-DesktopReleaseOverride 'true' 'Reason' 'push' }
Reject { Get-DesktopReleaseOverride 'true' "Reason`n::error::injected" 'workflow_dispatch' }
Reject { Get-DesktopReleaseOverride 'true' ('a'*501) 'workflow_dispatch' }
$recent = [DateTimeOffset]::UtcNow.AddHours(-24)
$stamp = [DateTimeOffset]::UtcNow.AddMinutes(-5).ToString('o')
$passing | Add-Member -NotePropertyName completed_at -NotePropertyValue $stamp
$hostedPass | Add-Member -NotePropertyName updated_at -NotePropertyValue $stamp
Verify ((Get-RequiredReleaseCheckState @($passing) $required $revision $recent) -eq 'Passed') 'Recent processor evidence should be reusable.'
Verify ((Get-HostedReleaseCheckState @($hostedPass) $revision $repository $recent) -eq 'Passed') 'Recent hosted evidence should be reusable.'
$passing.completed_at = [DateTimeOffset]::UtcNow.AddHours(-25).ToString('o')
$hostedPass.updated_at = $passing.completed_at
Verify ((Get-RequiredReleaseCheckState @($passing) $required $revision $recent) -eq 'Failed') 'Expired processor evidence cannot be reused.'
Verify ((Get-HostedReleaseCheckState @($hostedPass) $revision $repository $recent) -eq 'Failed') 'Expired hosted evidence cannot be reused.'
Verify (!(Test-RecentValidation '' $recent)) 'Missing completion timestamps cannot establish recent validation.'
Verify (!(Test-RecentValidation ([DateTimeOffset]::UtcNow.AddHours(1).ToString('o')) $recent)) 'Future timestamps cannot establish completed validation.'
Verify (Test-RecentValidation ($stamp | ConvertTo-Json | ConvertFrom-Json) $recent) 'PowerShell JSON timestamp conversion must retain freshness across cultures.'
Assert-ReusableValidationChanges @('README.md', 'RELEASE-NOTES.md', 'docfx_project/index.md', '.github/workflows/dotnet-publish.yml', '.github/scripts/Wait-RequiredReleaseChecks.ps1')
Verify $true 'Documentation and reviewed release automation may reuse validation.'
foreach ($path in @('src/ChangedRuntime.cs', 'TeslaPowerwallLibrary.Tests/LocalSolarMeterHoldTests.cs', 'TeslaPowerwallLibrary/TeslaPowerwallLibrary.csproj', 'Directory.Build.props', '.github/workflows/unit-tests.yml', '.github/workflows/unreviewed.yml')) {
    Reject { Assert-ReusableValidationChanges @($path) }
}
Reject { Assert-ReusableValidationChanges @('docfx_project/moved.cs', 'TeslaPowerwallLibrary/original.cs') }

# Exercise the actual script entry point with fake GitHub responses. No network or
# processor is used; an unexpected hardware lookup fails the test immediately.
$variables = @('REQUIRED_RELEASE_CHECKS','REQUIRED_HOSTED_WORKFLOWS','RELEASE_SKIP_HARDWARE_CHECKS','RELEASE_HARDWARE_SKIP_REASON','RELEASE_SKIP_DESKTOP_CHECKS','RELEASE_DESKTOP_SKIP_REASON','RELEASE_VALIDATION_COMMIT','GITHUB_EVENT_NAME','GITHUB_REPOSITORY','GITHUB_STEP_SUMMARY')
$saved = @{}
foreach ($name in $variables) { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }
$summaryPath = Join-Path ([IO.Path]::GetTempPath()) ('release-policy-' + [Guid]::NewGuid().ToString('N') + '.md')
$releasePolicyApi = [pscustomobject]@{ Calls=0; Outcome='success'; AllowHardware=$false; Ancestor=$true; ChangedPaths=@('README.md'); Expired=$false }
function git {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    $global:LASTEXITCODE = 0
    if ($Arguments[0] -eq 'merge-base') { if (!$releasePolicyApi.Ancestor) { $global:LASTEXITCODE=1 }; return }
    if ($Arguments[0] -eq '-c') { return $releasePolicyApi.ChangedPaths }
    return ('a'*40)
}
function gh {
    param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    $route = $Arguments -join ' '
    $evidenceRevision = if ($route -match '/commits/([a-f0-9]{40})/|head_sha=([a-f0-9]{40})') { $Matches[1] + $Matches[2] } else { 'a'*40 }
    $completed = [DateTimeOffset]::UtcNow.AddHours($(if ($releasePolicyApi.Expired) { -25 } else { -1 })).ToString('o')
    if ($route -match '/commits/') {
        if (!$releasePolicyApi.AllowHardware) { throw 'Hardware lookup occurred despite the explicit override.' }
        $releasePolicyApi.Calls++
        $global:LASTEXITCODE = 0
        return ConvertTo-Json -Depth 6 -InputObject @(@{check_runs=@(@{id=1;name='Processor tests / library';app=@{id=17};head_sha=$evidenceRevision;status='completed';conclusion=$releasePolicyApi.Outcome;completed_at=$completed})})
    }
    if ($route -notmatch '/actions/workflows/ci\.yml/runs\?') { throw 'Unexpected hosted workflow lookup.' }
    $releasePolicyApi.Calls++
    $global:LASTEXITCODE = 0
    return ConvertTo-Json -Depth 6 -InputObject @(@{workflow_runs=@(@{id=1;head_sha=$evidenceRevision;head_repository=@{full_name='example/library'};event='push';status='completed';conclusion=$releasePolicyApi.Outcome;updated_at=$completed})})
}
try {
    $env:REQUIRED_RELEASE_CHECKS = '[{"context":"Processor tests / library","appId":17}]'
    $env:REQUIRED_HOSTED_WORKFLOWS = '["ci.yml"]'
    $env:RELEASE_SKIP_HARDWARE_CHECKS = 'true'
    $env:RELEASE_HARDWARE_SKIP_REASON = 'Processor and local GitHub runner offline'
    $env:RELEASE_SKIP_DESKTOP_CHECKS = 'false'
    $env:RELEASE_DESKTOP_SKIP_REASON = ''
    $env:RELEASE_VALIDATION_COMMIT = ''
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    $env:GITHUB_REPOSITORY = 'example/library'
    $env:GITHUB_STEP_SUMMARY = $summaryPath
    $releasePolicyApi.Outcome = 'success'
    $releasePolicyApi.Calls = 0
    & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" | Out-Null
    Verify ($releasePolicyApi.Calls -eq 1) 'Offline release must still check hosted validation.'
    $summary = [IO.File]::ReadAllText($summaryPath)
    Verify ($summary.Contains($revision) -and $summary.Contains($env:RELEASE_HARDWARE_SKIP_REASON) -and $summary.Contains('does not establish a hardware-test pass')) 'Override evidence must identify the source, reason and unverified hardware status.'
    $releasePolicyApi.Outcome = 'failure'
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $env:REQUIRED_HOSTED_WORKFLOWS = ''
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $env:REQUIRED_HOSTED_WORKFLOWS = '["../unexpected.yml"]'
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $env:REQUIRED_HOSTED_WORKFLOWS = '["ci.yml"]'
    $env:RELEASE_SKIP_DESKTOP_CHECKS = 'true'
    $env:RELEASE_DESKTOP_SKIP_REASON = 'Exact source aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa; manual desktop results passed today; private report retained'
    $releasePolicyApi.Calls = 0
    & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" | Out-Null
    Verify ($releasePolicyApi.Calls -eq 0) 'Two explicit overrides must bypass both lookups.'
    $summary = [IO.File]::ReadAllText($summaryPath)
    Verify ($summary.Contains('does not establish a desktop-test pass') -and $summary.Contains($env:RELEASE_DESKTOP_SKIP_REASON)) 'Desktop attestation must be recorded without claiming a new pass.'
    $env:RELEASE_SKIP_HARDWARE_CHECKS = 'false'
    $releasePolicyApi.AllowHardware = $true
    $releasePolicyApi.Outcome = 'success'
    $releasePolicyApi.Calls = 0
    & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" | Out-Null
    Verify ($releasePolicyApi.Calls -eq 1) 'Desktop override must still verify the processor check.'
    $releasePolicyApi.Outcome = 'failure'
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $env:RELEASE_DESKTOP_SKIP_REASON = ''
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $env:RELEASE_SKIP_DESKTOP_CHECKS = 'false'
    $env:RELEASE_VALIDATION_COMMIT = 'b'*40
    $releasePolicyApi.Outcome = 'success'
    $releasePolicyApi.Calls = 0
    & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" | Out-Null
    Verify ($releasePolicyApi.Calls -eq 2) 'Recent CI reuse must verify both hosted and processor results for the selected ancestor.'
    $summary = [IO.File]::ReadAllText($summaryPath)
    Verify ($summary.Contains('Reusing validation completed within 24 hours for ' + ('b'*40)) -and $summary.Contains('Release source: ' + $revision)) 'CI reuse must record both tested and release revisions.'
    $releasePolicyApi.ChangedPaths = @('src/ChangedRuntime.cs')
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $releasePolicyApi.ChangedPaths = @('README.md')
    $releasePolicyApi.Ancestor = $false
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $releasePolicyApi.Ancestor = $true
    $releasePolicyApi.Expired = $true
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $releasePolicyApi.Expired = $false
    $env:RELEASE_VALIDATION_COMMIT = 'not-a-sha'
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
    $env:RELEASE_VALIDATION_COMMIT = 'b'*40
    $env:GITHUB_EVENT_NAME = 'push'
    Reject { & "$PSScriptRoot/Wait-RequiredReleaseChecks.ps1" }
} finally {
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name,$saved[$name]) }
    if (Test-Path -LiteralPath $summaryPath) { Remove-Item -LiteralPath $summaryPath }
    Remove-Item Function:\git,Function:\gh
}
Write-Output "Hosted validation and offline release policy: $checks scenarios passed."

Assert-ReusableValidationChanges @('.github/workflows/release-package.yml','.github/workflows/release-tests.yml')
Reject { Assert-ReusableValidationChanges @('.github/workflows/tests.yml') }
Reject { Assert-ReusableValidationChanges @('tools/BuildRelease.ps1') }
Reject { Assert-ReusableValidationChanges @('Directory.Build.props') }
Write-Output 'Shared policy path boundaries: 4 scenarios passed.'
