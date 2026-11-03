# Copyright (c) 2026 Neil Colvin. MIT License; see repository LICENSE.
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
$ErrorActionPreference='Stop'
& "$PSScriptRoot/../build/shared/driver-packaging/ILRepackMerge.ps1" @PSBoundParameters
