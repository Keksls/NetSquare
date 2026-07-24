# Packaging and publishing NetSquare

This document is the release checklist for `NetSquare.Core`, `NetSquare.Client`, and `NetSquare.Server`.

## Release rules

- Release all three packages with the same three-part version.
- Treat `NetSquare.Version.props` as the single version source.
- Keep Client and Server dependencies pinned to the exact matching Core version.
- Build packages only from a clean, reviewed Git revision.
- Never place a NuGet API key in the repository, a script, command history, or release notes.
- Publish `Core` before `Client` and `Server`.

A package version such as `1.0.15` produces package and informational version `1.0.15`, assembly and file version `1.0.15.0`, and exact Client and Server dependencies on `NetSquare.Core` version `[1.0.15]`.

The exact version relationship is required by the current handshake compatibility check.

## Before packaging

1. Confirm that the intended code is committed or otherwise reviewed.
2. Choose the next SemVer version:
   - patch for compatible fixes and performance improvements;
   - minor for compatible features;
   - major for breaking API or protocol changes.
3. Update `CHANGELOG.md`.
4. Update each package's `<releaseNotes>` in its `.nuspec`.
5. Decide whether the official artifacts should use light obfuscation.
6. Make sure the output directory is ignored by Git.

Use `Build\NuGet\<version>` for generated packages. The `Build` directory is already ignored.

## Create the packages

Always use `Pack-NetSquare.ps1`. It synchronizes version metadata and installation examples, builds every target framework, verifies the DLL versions, and creates all three packages.

```powershell
# Explicit versions are preferred for reproducible releases.
.\Pack-NetSquare.ps1 `
    -Version 1.0.15 `
    -Obfuscate `
    -OutputDirectory .\Build\NuGet\1.0.15
```

Other supported version modes:

```powershell
.\Pack-NetSquare.ps1                  # Patch
.\Pack-NetSquare.ps1 -Increment Minor
.\Pack-NetSquare.ps1 -Increment Major
```

`-Obfuscate` preserves public APIs, disables string hiding, and renames only private or internal implementation details in Core and Client. Server remains unobfuscated. Private mapping files are written below `Build\Obfuscated` and must never be published or committed.

Packaging performs Release builds but does not run `NetSquareDiagnostics` or the test projects.

If a build fails after version files were synchronized, fix the failure and rerun the script with the same explicit version.

## Verify before publishing

The output directory must contain exactly:

```text
NetSquare.Core.<version>.nupkg
NetSquare.Client.<version>.nupkg
NetSquare.Server.<version>.nupkg
```

Author-signing is not configured in this repository. `nuget verify -All` therefore reports `NU3004: The package is not signed`; do not treat that expected result as archive corruption. If author-signing is added later, verify every signed package before publication.

Inspect each archive directly and confirm:

- package ID, version, README, icon, description, and release notes;
- every expected target framework and DLL;
- exact `NetSquare.Core` dependencies in Client and Server;
- absence of PDBs, private mappings, secrets, and unrelated files.

Install the packages from the output directory as a local feed in representative applications before publishing. Exercise at least one TCP connection, one TCP-plus-UDP connection, message dispatch, clean disconnect, and every feature changed by the release.

Review `git diff` after packaging. Version metadata, package notes, documentation, and installation examples should be the only release-generated source changes.

## Prepare Git

Commit the reviewed release metadata and documentation, then create an annotated tag:

```powershell
git tag -a v1.0.15 -m "NetSquare 1.0.15"
```

Push the release commit and tag before publishing the immutable NuGet versions. If package publication later fails, fix the publication issue and retry the same artifacts; never reuse the version for different content.

## Configure the NuGet key

Use a nuget.org API key scoped only to the NetSquare packages and required operation. Store it as the Windows user environment variable `NUGET_API_KEY`.

A shell opened before the variable was created must read the user-scoped value explicitly:

```powershell
$nugetApiKey = [Environment]::GetEnvironmentVariable(
    "NUGET_API_KEY",
    [EnvironmentVariableTarget]::User)

if ([string]::IsNullOrWhiteSpace($nugetApiKey))
{
    throw "The user-scoped NUGET_API_KEY variable is missing."
}
```

Never print `$nugetApiKey`.

## Publish

NuGet package versions are immutable. Obtain an explicit publication confirmation immediately before running these commands:

```powershell
$source = "https://api.nuget.org/v3/index.json"
$directory = ".\Build\NuGet\1.0.15"

.\nuget6.exe push "$directory\NetSquare.Core.1.0.15.nupkg" `
    -Source $source -ApiKey $nugetApiKey -NonInteractive

.\nuget6.exe push "$directory\NetSquare.Client.1.0.15.nupkg" `
    -Source $source -ApiKey $nugetApiKey -NonInteractive

.\nuget6.exe push "$directory\NetSquare.Server.1.0.15.nupkg" `
    -Source $source -ApiKey $nugetApiKey -NonInteractive
```

After publishing:

1. Confirm all three package pages show the correct version and release notes.
2. Confirm Client and Server resolve the matching Core package.
3. Install from nuget.org in a clean sample project.
4. Publish the corresponding Git release notes from `CHANGELOG.md`.
5. Keep obfuscation mapping files private for support and crash analysis.
