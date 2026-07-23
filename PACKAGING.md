# Packaging NetSquare

`NetSquare.Version.props` is the single runtime version source for `NetSquare.Core`, `NetSquare.Client`, and `NetSquare.Server`. A NuGet version such as `1.0.15` produces:

- package version `1.0.15`;
- assembly version `1.0.15.0`;
- file version `1.0.15.0`;
- informational version `1.0.15`;
- exact Client and Server dependency on `NetSquare.Core` version `[1.0.15]`.

Always create releases through `Pack-NetSquare.ps1`. The script synchronizes the version sources, builds every target framework, verifies the generated DLL versions, and only then creates the three packages.

```powershell
# Increment the patch version, for example 1.0.14 -> 1.0.15.
.\Pack-NetSquare.ps1

# Increment another SemVer component.
.\Pack-NetSquare.ps1 -Increment Minor
.\Pack-NetSquare.ps1 -Increment Major

# Select an explicit three-part version.
.\Pack-NetSquare.ps1 -Version 2.0.0

# Create the normal packages with lightly obfuscated Client/Core internals.
.\Pack-NetSquare.ps1 -Obfuscate
```

`-Obfuscate` restores the repository-pinned stable Obfuscar tool, preserves every public API, disables string hiding, and only renames private/internal implementation details. The Server package remains unobfuscated. Generated DLLs and private mapping files are written below `Build\Obfuscated` and are not committed.

The obfuscation mode is opt-in: packaging without `-Obfuscate` keeps the existing standard Release behavior. The mapping files must remain private because they translate obfuscated symbols back to their original names.

If a build fails after the version files were synchronized, fix the failure and rerun the script with the explicit current version. For example:

```powershell
.\Pack-NetSquare.ps1 -Version 1.0.15
```

The packaging script performs builds but does not run the test projects.
