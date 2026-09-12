# Crestron Home NUnit v1.0.2

Patch release fixing test-input selections disappearing when switching suites in the same processor package.

## Windows runner

- Choose **Test inputs…** once for a processor package. All of its suites retain that selection, including unit, read-only live and control suites.
- Keep different processors and packages separate, and preserve inputs when a package reconnects on a new port.
- Migrate existing suite selections when they agree. If old suites have conflicting file selections, choose the intended files once for the package.
- Preserve **Clear inputs** across suite changes and restarts. Current input contents are transferred on each discovery or run; on supported hosts an empty selection clears the selected suite's old processor copy.

## Updating

Close the runner, extract the complete **CrestronHomeNUnit.Runner-win-x64.zip**, and start the new executable. The ZIP includes the .NET 10 Windows runtime. Existing user-profile settings and protected processor credentials are retained. The licensed GlyphLab icon remains embedded in the official application build; its source icon is not published.

Existing processor packages work with this runner update; **no processor redeployment is required**. The included NUnit Test Host package is rebuilt as **1.0.002.0000** for release consistency and has no host behavior changes. NUnit remains the official 4.6.1 package.

## Validation

The full runner and transport regression suite passed, including suite switching, processor/package isolation, existing-input migration, conflicting selections, clearing, reconnect, restart preferences and secure input transfer. Build completed with zero warnings or errors. Release CI also validates the included merged self-test and compatibility suites.

Documentation and SHA-256 checksums are included. No private settings, credentials or local test results are distributed. The existing NUnit repeated self-test limitation remains documented.
