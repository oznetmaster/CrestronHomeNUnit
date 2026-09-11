# Copyright (c) 2026 Neil Colvin.
# Licensed under the MIT License. See LICENSE file in the project root for full license information.

# Copyright © 2026 Neil Colvin.
# Licensed under the MIT License with Commons Clause. See LICENSE file in the project root for full license information.

# ManifestUtil.exe builds the .pkg (a zip archive) using backslashes as the directory separator for
# entries copied from the IncludeInPkg tree (e.g. "IncludeInPkg\UiDefinitions\UiDefinition.xml"). The
# zip file format spec requires forward slashes for entry names, and Crestron Home logs a warning
# ("appears to use backslashes as path separators") when it imports such a package. This script
# rewrites every entry name in the produced .pkg in-place, replacing backslashes with forward slashes.

param(
	[Parameter(Mandatory)][string] $PkgPath
)

if (-not (Test-Path $PkgPath)) {
	Write-Warning "FixPkgPathSeparators: '$PkgPath' does not exist - skipping."
	exit 0
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fullPath = (Resolve-Path $PkgPath).Path
$changed = 0

$archive = [System.IO.Compression.ZipFile]::Open($fullPath, [System.IO.Compression.ZipArchiveMode]::Update)
try {
	# ZipArchive doesn't allow renaming an entry in place, so any entry needing a fix is recreated
	# under the corrected (forward-slash) name and the original backslash-named entry is removed.
	$entriesNeedingFix = @($archive.Entries | Where-Object { $_.FullName.Contains('\') })

	foreach ($entry in $entriesNeedingFix) {
		$fixedName = $entry.FullName.Replace('\', '/')

		$newEntry = $archive.CreateEntry($fixedName, [System.IO.Compression.CompressionLevel]::Optimal)
		$sourceStream = $entry.Open()
		$destinationStream = $newEntry.Open()
		try {
			$sourceStream.CopyTo($destinationStream)
		}
		finally {
			$destinationStream.Dispose()
			$sourceStream.Dispose()
		}

		$entry.Delete()
		$changed++
	}
}
finally {
	$archive.Dispose()
}

Write-Host "FixPkgPathSeparators: $changed entry name(s) normalized to forward slashes in $fullPath"
