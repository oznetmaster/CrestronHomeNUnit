# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

# Adapted from the user's Entity V2 ILRepackMerge.ps1 workflow.
param(
    [Parameter(Mandatory)][string] $TargetPath,
    [Parameter(Mandatory)][string] $OutputPath,
    [Parameter(Mandatory)][string] $InputListFile,
    [Parameter(Mandatory)][string] $LibDir,
    [Parameter(Mandatory)][string] $ToolDirectory,
    [string] $SdkLibDir,
    [string] $FxRefDir,
    [string] $FxRuntimeDir
)
$ErrorActionPreference = 'Stop'
$inputs = @(Get-Content -LiteralPath $InputListFile | Where-Object { $_ -ne '' } | Select-Object -Unique)
if ($inputs.Count -eq 0 -or $inputs[0] -ne $TargetPath) { throw 'The driver must be the first merge input.' }
foreach ($inputPath in $inputs) {
    if (-not (Test-Path -LiteralPath $inputPath)) { throw "Missing merge input: $inputPath" }
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputPath))) | Out-Null
$mergeArguments = @('/internalize', '/allowdup', '/allowduplicateresources', "/out:$OutputPath")
foreach ($directoryPath in @($LibDir, $SdkLibDir, $FxRefDir, $FxRuntimeDir)) {
    if ($directoryPath -and (Test-Path -LiteralPath $directoryPath)) { $mergeArguments += "/lib:$directoryPath" }
}
$mergeArguments += $inputs
& dotnet (Join-Path $ToolDirectory 'ILRepackTool.dll') @mergeArguments
if ($LASTEXITCODE -ne 0) { throw "ILRepack failed with exit code $LASTEXITCODE" }
