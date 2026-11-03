# Copyright (c) 2026 Neil Colvin. MIT License with Commons Clause; see LICENSE in this directory.
param([Parameter(Mandatory)][string]$ToolDirectory)
$ErrorActionPreference='Stop'
$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture=Join-Path $tempRoot ('DriverPackagingChecks-'+[guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory($fixture)
$checks=0
try {
    $cecil=Join-Path $ToolDirectory 'Mono.Cecil.dll'
    Add-Type -Path $cecil
    $assembly=[Mono.Cecil.AssemblyDefinition]::CreateAssembly([Mono.Cecil.AssemblyNameDefinition]::new('PackagingFixture',[version]'1.0.0.0'),'PackagingFixture',[Mono.Cecil.ModuleKind]::Dll)
    $module=$assembly.MainModule
    foreach($entry in @(@('System','SyntheticMarker'),@('System','SR'),@('NUnit.Framework.Internal','AsyncToSyncAdapter'))) {
        $type=[Mono.Cecil.TypeDefinition]::new($entry[0],$entry[1],[Mono.Cecil.TypeAttributes]::NotPublic,$module.ImportReference([object]))
        $module.Types.Add($type)
        if($entry[1] -eq 'AsyncToSyncAdapter') {
            $method=[Mono.Cecil.MethodDefinition]::new('Read',[Mono.Cecil.MethodAttributes]::Static,$module.TypeSystem.Void)
            $il=$method.Body.GetILProcessor()
            $il.Emit([Mono.Cecil.Cil.OpCodes]::Ldstr,'System.Runtime.CompilerServices.AsyncStateMachineAttribute, System.Runtime')
            $il.Emit([Mono.Cecil.Cil.OpCodes]::Pop);$il.Emit([Mono.Cecil.Cil.OpCodes]::Ret)
            $type.Methods.Add($method)
        }
    }
    $anonymous=[Mono.Cecil.TypeDefinition]::new('','<>f__AnonymousType0',[Mono.Cecil.TypeAttributes]::NotPublic,$module.ImportReference([object]))
    $constructor=[Runtime.CompilerServices.CompilerGeneratedAttribute].GetConstructor([type[]]@())
    $anonymous.CustomAttributes.Add([Mono.Cecil.CustomAttribute]::new($module.ImportReference($constructor)))
    $getter=[Mono.Cecil.MethodDefinition]::new('get_Value',[Mono.Cecil.MethodAttributes]::Public,$module.TypeSystem.Int32)
    $getter.Body.GetILProcessor().Emit([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1)
    $getter.Body.GetILProcessor().Emit([Mono.Cecil.Cil.OpCodes]::Ret)
    $anonymous.Methods.Add($getter)
    $property=[Mono.Cecil.PropertyDefinition]::new('Value',[Mono.Cecil.PropertyAttributes]::None,$module.TypeSystem.Int32)
    $property.GetMethod=$getter;$anonymous.Properties.Add($property);$module.Types.Add($anonymous)
    $owner='{"SchemaVersion":"2.0","GeneralInformation":{"Guid":"owner-guid"}}'
    $dependency='{"SchemaVersion":"2.0","GeneralInformation":{"Guid":"dependency-guid"}}'
    foreach($entry in @(@('Owner.json',$owner),@('Dependency.json',$dependency))) {
        $module.Resources.Add([Mono.Cecil.EmbeddedResource]::new($entry[0],[Mono.Cecil.ManifestResourceAttributes]::Private,[Text.Encoding]::UTF8.GetBytes($entry[1])))
    }
    $source=Join-Path $fixture 'source.dll';$assembly.Write($source);$assembly.Dispose()
    $manifest=Join-Path $fixture 'manifest.json';[IO.File]::WriteAllText($manifest,$owner)
    $inputs=Join-Path $fixture 'inputs.txt';[IO.File]::WriteAllText($inputs,$source)
    $merged=Join-Path $fixture 'merged/fixture.dll'
    & "$PSScriptRoot/ILRepackMerge.ps1" -TargetPath $source -OutputPath $merged -InputListFile $inputs -LibDir $PSHOME -ToolDirectory $ToolDirectory -FxRuntimeDir $PSHOME
    if(!(Test-Path -LiteralPath $merged)){throw 'Merge output missing.'};$checks++
    $patched=Join-Path $fixture 'patched/fixture.dll'
    & "$PSScriptRoot/PatchMergedAssembly.ps1" -AssemblyPath $merged -OutputPath $patched -CecilPath $cecil -InputListFile $inputs -ManifestPath $manifest
    $result=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($patched)
    try {
        if(!$result.MainModule.GetType('_Stripped.System.SyntheticMarker')){throw 'Restricted namespace was not patched.'};$checks++
        if(@($result.MainModule.Resources | Where-Object Name -eq 'Dependency.json').Count){throw 'Dependency manifest retained.'};$checks++
        if(@($result.MainModule.Resources | Where-Object Name -eq 'Owner.json').Count -ne 1){throw 'Owner manifest lost.'};$checks++
        if(!@($result.MainModule.Types | Where-Object Namespace -like '_MergedResources.*').Count){throw 'Private resource helper was not isolated.'};$checks++
        $anonymous=$result.MainModule.GetType('_MergedAnonymousTypes.PackagingFixture.<>f__AnonymousType0')
        if(!$anonymous -or @($anonymous.Properties|Where-Object Name -eq 'Value').Count -ne 1){throw 'Anonymous reflection metadata was lost.'};$checks++
        $type=$result.MainModule.GetType('NUnit.Framework.Internal.AsyncToSyncAdapter')
        $literal=@($type.Methods[0].Body.Instructions | Where-Object { $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldstr })[0].Operand
        if($literal -cne 'System.Runtime.CompilerServices.AsyncStateMachineAttribute, mscorlib'){throw 'Async runtime correction was lost.'};$checks++
    }finally{$result.Dispose()}
    [IO.File]::WriteAllText($manifest,'{"SchemaVersion":"2.0","GeneralInformation":{"Guid":"missing-guid"}}')
    & pwsh -NoLogo -NoProfile -File "$PSScriptRoot/PatchMergedAssembly.ps1" -AssemblyPath $source -OutputPath (Join-Path $fixture 'missing-owner.dll') -CecilPath $cecil -InputListFile $inputs -ManifestPath $manifest 2>&1 | Out-Null
    if($LASTEXITCODE -eq 0){throw 'Missing package owner was accepted.'}
    # The rejected child was intentional; do not propagate its exit code to GitHub's wrapper.
    $global:LASTEXITCODE=0
    $checks++
    Write-Host "Passed $checks native merge/patch fixture checks. No driver or device was deployed."
}finally{
    $resolved=[IO.Path]::GetFullPath($fixture)
    if([IO.Path]::GetDirectoryName($resolved).TrimEnd('\','/') -ne $tempRoot.TrimEnd('\','/') -or [IO.Path]::GetFileName($resolved) -notlike 'DriverPackagingChecks-*'){throw 'Unexpected fixture cleanup path.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
