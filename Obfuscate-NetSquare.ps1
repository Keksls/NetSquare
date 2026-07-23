[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "Build\Obfuscated")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$toolManifestPath = Join-Path $PSScriptRoot ".config\dotnet-tools.json"
$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)

if (-not (Test-Path -LiteralPath $toolManifestPath -PathType Leaf))
{
    throw "The local .NET tool manifest was not found: $toolManifestPath"
}

$targets = @(
    [PSCustomObject]@{
        Framework = "netstandard2.0"
        CoreDirectory = "NetSquareCore\bin\Release\netstandard2.0"
        ClientDirectory = "NetSquareClient\bin\Release\netstandard2.0"
    },
    [PSCustomObject]@{
        Framework = "net8.0"
        CoreDirectory = "NetSquareCore\bin\Release\net8.0"
        ClientDirectory = "NetSquareClient\bin\Release\net8.0"
    },
    [PSCustomObject]@{
        Framework = "net48"
        CoreDirectory = "NetSquareCore\bin\Release\net48"
        ClientDirectory = "NetSquareClient\bin\Release\net48"
    }
)

# Restore the repository-pinned stable tool only when obfuscation is explicitly requested.
Push-Location $PSScriptRoot
try
{
    & dotnet tool restore --tool-manifest $toolManifestPath
    if ($LASTEXITCODE -ne 0)
    {
        throw "Obfuscar tool restore failed."
    }

    foreach ($target in $targets)
    {
        $coreDirectory = [System.IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot $target.CoreDirectory))
        $clientDirectory = [System.IO.Path]::GetFullPath(
            (Join-Path $PSScriptRoot $target.ClientDirectory))
        $coreAssemblyPath = Join-Path $coreDirectory "NetSquareCore.dll"
        $clientAssemblyPath = Join-Path $clientDirectory "NetSquareClient.dll"

        if (-not (Test-Path -LiteralPath $coreAssemblyPath -PathType Leaf))
        {
            throw "Core assembly not found: $coreAssemblyPath"
        }
        if (-not (Test-Path -LiteralPath $clientAssemblyPath -PathType Leaf))
        {
            throw "Client assembly not found: $clientAssemblyPath"
        }

        $frameworkWorkDirectory = Join-Path $resolvedOutputDirectory "work\$($target.Framework)"
        $frameworkMapDirectory = Join-Path $resolvedOutputDirectory "maps\$($target.Framework)"
        $configurationPath = Join-Path $frameworkWorkDirectory "obfuscar.xml"
        $mappingPath = Join-Path $frameworkMapDirectory "mapping.xml"

        [System.IO.Directory]::CreateDirectory($frameworkWorkDirectory) | Out-Null
        [System.IO.Directory]::CreateDirectory($frameworkMapDirectory) | Out-Null

        # Generate an absolute-path configuration so the pipeline is ready for future Obfuscar versions.
        $xmlSettings = [System.Xml.XmlWriterSettings]::new()
        $xmlSettings.Indent = $true
        $xmlSettings.Encoding = $utf8WithoutBom
        $writer = [System.Xml.XmlWriter]::Create($configurationPath, $xmlSettings)
        try
        {
            $writer.WriteStartDocument()
            $writer.WriteStartElement("Obfuscator")

            $variables = [ordered]@{
                InPath = $coreDirectory
                OutPath = $frameworkWorkDirectory
                LogFile = $mappingPath
                XmlMapping = "true"
                KeepPublicApi = "true"
                HidePrivateApi = "true"
                HideStrings = "false"
                SuppressIldasm = "false"
                UseUnicodeNames = "false"
                ReuseNames = "true"
                SkipGenerated = "true"
            }

            foreach ($variable in $variables.GetEnumerator())
            {
                $writer.WriteStartElement("Var")
                $writer.WriteAttributeString("name", $variable.Key)
                $writer.WriteAttributeString("value", [string]$variable.Value)
                $writer.WriteEndElement()
            }

            foreach ($searchPath in @($coreDirectory, $clientDirectory))
            {
                $writer.WriteStartElement("AssemblySearchPath")
                $writer.WriteAttributeString("path", $searchPath)
                $writer.WriteEndElement()
            }

            foreach ($modulePath in @($coreAssemblyPath, $clientAssemblyPath))
            {
                $writer.WriteStartElement("Module")
                $writer.WriteAttributeString("file", $modulePath)
                $writer.WriteEndElement()
            }

            $writer.WriteEndElement()
            $writer.WriteEndDocument()
        }
        finally
        {
            $writer.Dispose()
        }

        & dotnet tool run obfuscar.console -- $configurationPath
        if ($LASTEXITCODE -ne 0)
        {
            throw "Obfuscation failed for $($target.Framework)."
        }

        $obfuscatedCorePath = Join-Path $frameworkWorkDirectory "NetSquareCore.dll"
        $obfuscatedClientPath = Join-Path $frameworkWorkDirectory "NetSquareClient.dll"
        if (-not (Test-Path -LiteralPath $obfuscatedCorePath -PathType Leaf))
        {
            throw "Obfuscar did not produce the expected Core assembly: $obfuscatedCorePath"
        }
        if (-not (Test-Path -LiteralPath $obfuscatedClientPath -PathType Leaf))
        {
            throw "Obfuscar did not produce the expected Client assembly: $obfuscatedClientPath"
        }

        $coreDestinationDirectory = Join-Path $resolvedOutputDirectory "NetSquareCore\$($target.Framework)"
        $clientDestinationDirectory = Join-Path $resolvedOutputDirectory "NetSquareClient\$($target.Framework)"
        [System.IO.Directory]::CreateDirectory($coreDestinationDirectory) | Out-Null
        [System.IO.Directory]::CreateDirectory($clientDestinationDirectory) | Out-Null

        Copy-Item -LiteralPath $obfuscatedCorePath `
            -Destination (Join-Path $coreDestinationDirectory "NetSquareCore.dll") `
            -Force
        Copy-Item -LiteralPath $obfuscatedClientPath `
            -Destination (Join-Path $clientDestinationDirectory "NetSquareClient.dll") `
            -Force
    }
}
finally
{
    Pop-Location
}

Write-Host "Light NetSquare Client/Core obfuscation completed: $resolvedOutputDirectory"
