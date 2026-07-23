[CmdletBinding()]
param(
    [ValidateSet("Patch", "Minor", "Major")]
    [string]$Increment = "Patch",

    [string]$Version,

    [string]$OutputDirectory = $PSScriptRoot,

    [switch]$Obfuscate
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$versionPropsPath = Join-Path $PSScriptRoot "NetSquare.Version.props"
$nugetPath = Join-Path $PSScriptRoot "nuget6.exe"

if (-not (Test-Path -LiteralPath $nugetPath -PathType Leaf))
{
    throw "NuGet executable not found: $nugetPath"
}

$versionPropsContent = [System.IO.File]::ReadAllText($versionPropsPath)
$versionMatch = [System.Text.RegularExpressions.Regex]::Match(
    $versionPropsContent,
    "<NetSquareVersion>(?<Version>\d+\.\d+\.\d+)</NetSquareVersion>")

if (-not $versionMatch.Success)
{
    throw "NetSquare.Version.props does not contain a valid three-part NetSquareVersion."
}

$currentVersion = [System.Version]::Parse($versionMatch.Groups["Version"].Value)

if ([string]::IsNullOrWhiteSpace($Version))
{
    switch ($Increment)
    {
        "Major" { $targetVersion = [System.Version]::new($currentVersion.Major + 1, 0, 0) }
        "Minor" { $targetVersion = [System.Version]::new($currentVersion.Major, $currentVersion.Minor + 1, 0) }
        default { $targetVersion = [System.Version]::new($currentVersion.Major, $currentVersion.Minor, $currentVersion.Build + 1) }
    }
}
else
{
    if ($Version -notmatch "^\d+\.\d+\.\d+$")
    {
        throw "Version must use the Major.Minor.Patch format."
    }

    $targetVersion = [System.Version]::Parse($Version)
    if ($targetVersion -lt $currentVersion)
    {
        throw "Version $targetVersion is older than the current version $currentVersion."
    }
}

$targetVersionText = $targetVersion.ToString(3)
$targetAssemblyVersion = "$targetVersionText.0"
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

# Synchronize the central version before building so every produced assembly uses it.
$updatedPropsContent = $versionMatch.Result(
    "<NetSquareVersion>$targetVersionText</NetSquareVersion>")
$updatedPropsContent = $versionPropsContent.Substring(0, $versionMatch.Index) +
    $updatedPropsContent +
    $versionPropsContent.Substring($versionMatch.Index + $versionMatch.Length)
[System.IO.File]::WriteAllText($versionPropsPath, $updatedPropsContent, $utf8WithoutBom)

$nuspecPaths = @(
    (Join-Path $PSScriptRoot "NetSquareCore\NetSquareCore.nuspec"),
    (Join-Path $PSScriptRoot "NetSquareClient\NetSquareClient.nuspec"),
    (Join-Path $PSScriptRoot "NetSquareServer\NetSquareServer.nuspec")
)

# Keep checked-in package metadata and exact Core dependencies synchronized with the assemblies.
foreach ($nuspecPath in $nuspecPaths)
{
    $nuspecContent = [System.IO.File]::ReadAllText($nuspecPath)
    $nuspecContent = [System.Text.RegularExpressions.Regex]::Replace(
        $nuspecContent,
        "<version>[^<]+</version>",
        "<version>$targetVersionText</version>",
        1)
    $nuspecContent = [System.Text.RegularExpressions.Regex]::Replace(
        $nuspecContent,
        '(<dependency\s+id="NetSquare\.Core"\s+version=")\[[^\]]+\]("\s*/>)',
        "`$1[$targetVersionText]`$2")
    [System.IO.File]::WriteAllText($nuspecPath, $nuspecContent, $utf8WithoutBom)
}

$readmePaths = @(
    (Join-Path $PSScriptRoot "NetSquareCore\README.md"),
    (Join-Path $PSScriptRoot "NetSquareClient\README.md"),
    (Join-Path $PSScriptRoot "NetSquareServer\README.md")
)

# Refresh installation examples without replacing unrelated version numbers in documentation.
foreach ($readmePath in $readmePaths)
{
    $readmeContent = [System.IO.File]::ReadAllText($readmePath)
    $readmeContent = [System.Text.RegularExpressions.Regex]::Replace(
        $readmeContent,
        '(Install-Package\s+NetSquare\.[A-Za-z]+\s+-Version\s+)\d+\.\d+\.\d+',
        "`$1$targetVersionText")
    $readmeContent = [System.Text.RegularExpressions.Regex]::Replace(
        $readmeContent,
        '(dotnet\s+add\s+package\s+NetSquare\.[A-Za-z]+\s+--version\s+)\d+\.\d+\.\d+',
        "`$1$targetVersionText")
    [System.IO.File]::WriteAllText($readmePath, $readmeContent, $utf8WithoutBom)
}

$projectPaths = @(
    (Join-Path $PSScriptRoot "NetSquareCore\NetSquareCore.csproj"),
    (Join-Path $PSScriptRoot "NetSquareClient\NetSquareClient.csproj"),
    (Join-Path $PSScriptRoot "NetSquareServer\NetSquareServer.csproj")
)

foreach ($projectPath in $projectPaths)
{
    & dotnet build $projectPath --configuration Release -p:NetSquareVersion=$targetVersionText
    if ($LASTEXITCODE -ne 0)
    {
        throw "Release build failed for $projectPath."
    }
}

$assemblyPaths = @(
    "NetSquareCore\bin\Release\netstandard2.0\NetSquareCore.dll",
    "NetSquareCore\bin\Release\net8.0\NetSquareCore.dll",
    "NetSquareCore\bin\Release\net48\NetSquareCore.dll",
    "NetSquareClient\bin\Release\netstandard2.0\NetSquareClient.dll",
    "NetSquareClient\bin\Release\net8.0\NetSquareClient.dll",
    "NetSquareClient\bin\Release\net48\NetSquareClient.dll",
    "NetSquareServer\bin\Release\netstandard2.0\NetSquare_Server.dll",
    "NetSquareServer\bin\Release\net8.0-windows7.0\NetSquare_Server.dll",
    "NetSquareServer\bin\Release\net48\NetSquare_Server.dll"
)

# Abort packaging if even one target framework produced a mismatched assembly.
foreach ($relativeAssemblyPath in $assemblyPaths)
{
    $assemblyPath = Join-Path $PSScriptRoot $relativeAssemblyPath
    $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
    if ($assemblyName.Version.ToString() -ne $targetAssemblyVersion)
    {
        throw "Assembly version mismatch for ${assemblyPath}: expected $targetAssemblyVersion, found $($assemblyName.Version)."
    }

    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).FileVersion
    if ($fileVersion -ne $targetAssemblyVersion)
    {
        throw "File version mismatch for ${assemblyPath}: expected $targetAssemblyVersion, found $fileVersion."
    }
}

$coreAssemblyRoot = "bin\Release"
$clientAssemblyRoot = "bin\Release"

if ($Obfuscate)
{
    $obfuscationScriptPath = Join-Path $PSScriptRoot "Obfuscate-NetSquare.ps1"
    $obfuscationOutputDirectory = Join-Path $PSScriptRoot "Build\Obfuscated"

    & $obfuscationScriptPath -OutputDirectory $obfuscationOutputDirectory
    if ($LASTEXITCODE -ne 0)
    {
        throw "NetSquare Client/Core obfuscation failed."
    }

    $coreAssemblyRoot = Join-Path $obfuscationOutputDirectory "NetSquareCore"
    $clientAssemblyRoot = Join-Path $obfuscationOutputDirectory "NetSquareClient"
}

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null

foreach ($nuspecPath in $nuspecPaths)
{
    $packageID = [System.IO.Path]::GetFileNameWithoutExtension($nuspecPath)
    $assemblyRoot = switch ($packageID)
    {
        "NetSquareCore" { $coreAssemblyRoot }
        "NetSquareClient" { $clientAssemblyRoot }
        default { "bin\Release" }
    }

    & $nugetPath pack $nuspecPath `
        -Version $targetVersionText `
        -Properties "AssemblyRoot=$assemblyRoot" `
        -OutputDirectory $resolvedOutputDirectory `
        -NonInteractive

    if ($LASTEXITCODE -ne 0)
    {
        throw "NuGet packaging failed for $nuspecPath."
    }
}

$packageFlavor = if ($Obfuscate) { "lightly obfuscated" } else { "standard" }
Write-Host "NetSquare $targetVersionText $packageFlavor assemblies and NuGet packages were created successfully."
